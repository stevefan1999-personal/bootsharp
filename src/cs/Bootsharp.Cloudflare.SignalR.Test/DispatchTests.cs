using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The message switch, arm for arm against upstream's <c>DispatchMessageAsync</c>
/// (<c>DefaultHubDispatcher.cs:199-280</c>), plus the routing rules of the generated table. The arms
/// whose feature is skipped answer the client rather than going quiet: rule is that an
/// unsupported feature fails loudly at compile time or at the protocol, never silently.
/// </summary>
public class DispatchTests
{
    private readonly FakeTransport transport = new();
    private readonly HubRecorder recorder = new();
    private readonly HubConnectionHandler<TestHub> handler;

    public DispatchTests ()
    {
        handler = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
    }

    [Fact]
    public async Task InvocationWithArgumentsCompletesWithTheResult ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "Echo", "\"hello\""));
        Assert.Equal("hello", Result(Last("c1")));
    }

    [Fact]
    public async Task ArgumentsAreBoundToTheirDeclaredTypesRatherThanToJsonElements ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "Add", "2, 40"));
        Assert.Equal(42, Last("c1").GetProperty("result").GetInt32());
    }

    [Fact]
    public async Task NonBlockingInvocationGetsNoCompletion ()
    {
        // No invocation id: the client is not waiting for one, and inventing an id would fail its
        // own lookup. Upstream behaves identically.
        await Connect("c1");
        await Send("c1", "{\"type\":1,\"target\":\"Notify\",\"arguments\":[\"quiet\"]}");
        Assert.Equal(["notify:quiet"], recorder.Calls);
        Assert.DoesNotContain(transport.MessagesOf("c1"), static m => m != "{}");
    }

    [Fact]
    public async Task HubMethodNameRoutesByTheWireNameNotTheClrName ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "client.ping", ""));
        Assert.Equal("pong", Result(Last("c1")));
    }

    [Fact]
    public async Task ClrNameOfARenamedMethodIsNotDispatchable ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "Renamed", ""));
        Assert.Equal("Unknown hub method 'Renamed'.", Error(Last("c1")));
    }

    [Fact]
    public async Task SlotLookupIsCaseInsensitive ()
    {
        // Upstream keys its descriptor dictionary with StringComparer.OrdinalIgnoreCase
        // (DefaultHubDispatcher.cs:25), and a client that lower-cases targets is common.
        await Connect("c1");
        await Send("c1", Invocation("1", "eChO", "\"case\""));
        Assert.Equal("case", Result(Last("c1")));
    }

    [Fact]
    public async Task UnknownTargetCompletesWithAnErrorRatherThanClosing ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "Missing", ""));
        Assert.Equal("Unknown hub method 'Missing'.", Error(Last("c1")));
        Assert.Empty(transport.Closes);
    }

    [Fact]
    public async Task HubExceptionMessageReachesTheClientVerbatim ()
    {
        // The one exception type whose message is a contract: upstream forwards it regardless of
        // EnableDetailedErrors, and so does this host.
        await Connect("c1");
        await Send("c1", Invocation("1", "Refuse", ""));
        Assert.Equal("refused by contract", Error(Last("c1")));
    }

    [Fact]
    public async Task OtherExceptionDetailIsWithheldUnlessDetailedErrorsAreEnabled ()
    {
        await Connect("c1");
        await Send("c1", Invocation("1", "Explode", ""));
        var error = Error(Last("c1"));
        Assert.Equal("An unexpected error occurred invoking 'Explode' on the server.", error);
        Assert.DoesNotContain("internal detail", error);
    }

    [Fact]
    public async Task DetailedErrorsAppendTheExceptionTypeAndMessage ()
    {
        var detailed = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport,
            new HubOptions { EnableDetailedErrors = true });
        transport.Accept("c1");
        await detailed.AcceptAsync("c1");
        await detailed.ReceiveAsync("c1", Wire.HandshakeRequest);
        await detailed.ReceiveAsync("c1", Wire.Frame(Invocation("1", "Explode", "")));
        Assert.Contains("InvalidOperationException: internal detail", Error(Last("c1")));
    }

    [Fact]
    public async Task PingIsSilent ()
    {
        // Normally absorbed by workerd's auto-responder without waking the actor at all; one that
        // reaches here is a liveness signal the sweep reads from the timestamp instead.
        await Connect("c1");
        var before = transport.Sent.Count;
        await Send("c1", "{\"type\":6}");
        Assert.Equal(before, transport.Sent.Count);
    }

    [Fact]
    public async Task CloseAbortsTheConnection ()
    {
        await Connect("c1");
        var aborted = false;
        // The abort has to be observed through the connection the handler actually dispatches on,
        // which is the one the lifetime manager holds — a rebuilt context would prove nothing.
        var lifetime = (DurableObjectHubLifetimeManager<TestHub>)handler.Lifetime;
        Assert.True(lifetime.TryGetConnection("c1", out var connection));
        connection.ConnectionAborted.Register(() => aborted = true);
        await Send("c1", "{\"type\":7}");
        Assert.True(aborted);
    }

    [Fact]
    public async Task StreamInvocationIsRefusedWithANamedCompletionError ()
    {
        await Connect("c1");
        await Send("c1", "{\"type\":4,\"invocationId\":\"1\",\"target\":\"Echo\",\"arguments\":[\"x\"]}");
        Assert.Contains("Streaming hub methods are not supported", Error(Last("c1")));
    }

    [Fact]
    public async Task BindingFailureIsReportedAgainstTheInvocationRatherThanThrown ()
    {
        // A client sending a string where the hub declares an int: the parser produces an
        // InvocationBindingFailureMessage, and the invocation id has to survive into the completion.
        await Connect("c1");
        await Send("c1", Invocation("1", "Add", "\"not a number\", 1"));
        Assert.Contains("Failed to invoke 'Add'", Error(Last("c1")));
    }

    [Fact]
    public async Task CallerContextCarriesTheConnectionId ()
    {
        await Connect("c7");
        await Send("c7", Invocation("1", "Whoami", ""));
        Assert.Equal("c7", Result(Last("c7")));
    }

    private async Task Connect (string connectionId)
    {
        transport.Accept(connectionId);
        await handler.AcceptAsync(connectionId);
        await handler.ReceiveAsync(connectionId, Wire.HandshakeRequest);
    }

    private Task Send (string connectionId, string json) =>
        handler.ReceiveAsync(connectionId, Wire.Frame(json));

    private JsonElement Last (string connectionId) =>
        JsonDocument.Parse(transport.MessagesOf(connectionId)[^1]).RootElement;

    private static string? Result (JsonElement completion) => completion.GetProperty("result").GetString();

    private static string Error (JsonElement completion) => completion.GetProperty("error").GetString()!;

    private static string Invocation (string id, string target, string args) =>
        $"{{\"type\":1,\"invocationId\":\"{id}\",\"target\":\"{target}\",\"arguments\":[{args}]}}";
}
