using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// The reason every streaming-shaped member of this layer throws, worded once so the message a
/// developer sees names the milestone that lifts the restriction rather than just failing.
/// </summary>
internal static class Streaming
{
    internal const string Reason =
        "Streaming request and response bodies need Tier-1 JS handles; " +
        "until they land, Bootsharp.Cloudflare.AspNetCore buffers bodies and this member is unavailable.";

    internal static PlatformNotSupportedException Unsupported (string member) =>
        new($"{member} is not supported on Cloudflare Workers. {Reason}");
}

/// <summary>
/// <see cref="IHttpRequestFeature"/> over the buffered snapshot taken from the live workerd
/// <c>Request</c> handle.
/// </summary>
/// <remarks>
/// Kestrel's <c>HttpRequestFeature</c> exists to be reset and reused across requests on a pooled
/// connection. workerd hands each event a fresh object graph behind the serialization gate, so
/// there is nothing to pool and the feature is a plain property bag ( verdict:
/// REIMPLEMENT the Default* trio.
/// </remarks>
internal sealed class WorkerRequestFeature : IHttpRequestFeature, IHttpRequestBodyDetectionFeature
{
    public string Protocol { get; set; } = "HTTP/1.1";
    public string Scheme { get; set; } = "https";
    public string Method { get; set; } = HttpMethods.Get;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get; set; } = string.Empty;
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;

    /// <summary>
    /// Whether a body may be read. Without this feature the body binding the generator emits treats
    /// every request as bodyless, which is why it is one of the four mandatory
    /// features rather than an optional one.
    /// </summary>
    public bool CanHaveBody { get; set; }
}

/// <summary>
/// <see cref="IHttpResponseFeature"/> plus <see cref="IHttpResponseBodyFeature"/> over a buffer that
/// becomes the <see cref="HttpResponseData"/> snapshot when the event ends.
/// </summary>
/// <remarks>
/// There is no socket to flush to, so "has started" can only become true once the snapshot is
/// taken. That makes <c>OnStarting</c> callbacks run exactly once, at snapshot time, in
/// registration order — the ordering ASP.NET Core promises — and makes every member whose contract
/// is "commit the response now and keep writing" unimplementable rather than merely unimplemented.
/// </remarks>
internal sealed class WorkerResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
{
    private readonly MemoryStream buffer = new();
    private List<(Func<object, Task> Callback, object State)>? starting;
    private List<(Func<object, Task> Callback, object State)>? completed;
    private PipeWriter? writer;

    public int StatusCode { get; set; } = StatusCodes.Status200OK;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public bool HasStarted { get; private set; }

    public Stream Body
    {
        get => buffer;
        set => throw Streaming.Unsupported("Replacing HttpResponse.Body");
    }

    Stream IHttpResponseBodyFeature.Stream => buffer;

    // Buffered, so a PipeWriter over the same MemoryStream is exact rather than a stand-in: the
    // vendored WriteAsync extensions go through BodyWriter for larger payloads.
    public PipeWriter Writer => writer ??= PipeWriter.Create(buffer, new StreamPipeWriterOptions(leaveOpen: true));

    public void OnStarting (Func<object, Task> callback, object state) =>
        (starting ??= []).Add((callback, state));

    public void OnCompleted (Func<object, Task> callback, object state) =>
        (completed ??= []).Add((callback, state));

    /// <summary>
    /// Throws: buffering is the whole model here, so a caller asking for it to stop is asking for
    /// something that cannot be delivered.
    /// </summary>
    /// <remarks>This is the call server-sent events make before writing their first frame. Failing
    /// it is the difference between "SSE is not supported yet" and an SSE response that silently
    /// arrives all at once, at the end.</remarks>
    public void DisableBuffering () => throw Streaming.Unsupported("Response buffering cannot be disabled");

    /// <summary>Completes without doing anything: nothing is transmitted until the event ends.</summary>
    /// <remarks>
    /// The one place this diverges from ASP.NET Core in a way a handler could notice: there,
    /// starting the response makes the headers immutable. Here it cannot, because there is no
    /// socket the headers have gone out on — they are rendered from
    /// <see cref="Headers"/> when the snapshot is taken. Handlers that write with
    /// <c>WriteAsync</c> go through this call, so throwing would make the most ordinary thing a
    /// handler can do fail.
    /// </remarks>
    public Task StartAsync (CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendFileAsync (string path, long offset, long? count, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException(
            "File results are not supported on Cloudflare Workers: a worker has no filesystem. " +
            "Serve static content from the assets binding, or return the bytes with TypedResults.Content.");

    /// <summary>Completes without doing anything — see <see cref="StartAsync"/>.</summary>
    public Task CompleteAsync () => Task.CompletedTask;

    /// <summary>
    /// Runs the <c>OnStarting</c> callbacks, freezes the response and renders the snapshot; then
    /// runs the <c>OnCompleted</c> callbacks.
    /// </summary>
    internal async Task<HttpResponseData> SnapshotAsync ()
    {
        if (starting is { } startingCallbacks)
            foreach (var (callback, state) in startingCallbacks)
                await callback(state);
        HasStarted = true;
        if (writer is not null) await writer.FlushAsync();
        var snapshot = new HttpResponseData(StatusCode, HeaderJson.Render(Headers), ReadBody());
        if (completed is { } completedCallbacks)
            for (var index = completedCallbacks.Count - 1; index >= 0; index--)
                await completedCallbacks[index].Callback(completedCallbacks[index].State);
        return snapshot;
    }

    private string ReadBody () =>
        buffer.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
}

/// <summary>Per-request feature slots that are plain storage.</summary>
internal sealed class WorkerRequestStateFeature :
    IItemsFeature, IQueryFeature, IRouteValuesFeature, IEndpointFeature,
    IServiceProvidersFeature, IHttpRequestLifetimeFeature, IHttpRequestIdentifierFeature
{
    private IDictionary<object, object?>? items;
    private RouteValueDictionary? routeValues;

    public IDictionary<object, object?> Items
    {
        get => items ??= new Dictionary<object, object?>();
        set => items = value;
    }

    public IQueryCollection Query { get; set; } = QueryCollection.Empty;

    public RouteValueDictionary RouteValues
    {
        get => routeValues ??= [];
        set => routeValues = value;
    }

    public Endpoint? Endpoint { get; set; }
    public IServiceProvider RequestServices { get; set; } = null!;
    public string TraceIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// workerd cancels by discarding the isolate rather than by signalling, and a worker cannot
    /// observe client disconnect, so this token is never signalled. It is a real token rather than
    /// <see cref="CancellationToken.None"/> so that handler code storing or linking it behaves
    /// which means it has to come from a source, created on first read rather than per request, so a
    /// handler that never asks for one pays nothing.
    /// </summary>
    public CancellationToken RequestAborted
    {
        get => aborted ??= (source ??= new CancellationTokenSource()).Token;
        set => aborted = value;
    }

    private CancellationTokenSource? source;
    private CancellationToken? aborted;

    public void Abort () => throw new PlatformNotSupportedException(
        "A Cloudflare Worker cannot abort an in-flight response: the runtime owns the socket. " +
        "Return an error result instead.");
}
