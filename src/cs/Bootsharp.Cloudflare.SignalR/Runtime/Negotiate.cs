using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// The negotiate endpoint, in the four fields the JS client actually reads. Field names are fixed by
/// <c>NegotiateProtocol</c> (<c>common/Http.Connections.Common/src/NegotiateProtocol.cs:19-40</c>);
/// the ~4.2 KLOC of <c>Http.Connections</c> around them exists to serve long polling and SSE, which
/// this host does not have.
/// </summary>
/// <remarks>
/// <para><b>Why ship it at all, given <c>skipNegotiation</c> works.</b> Because
/// <c>connectionToken</c> is the natural carrier for Durable Object routing: negotiate runs in the
/// <i>Worker</i>, does the auth, picks the scope, and hands back an opaque token; the client then
/// connects to <c>/hub?id=&lt;token&gt;</c> (<c>HttpConnection.ts:381-387</c>) and the Worker routes
/// on the token. That is strictly better than putting the shard in the URL for anyone who cares
/// about auth or about hiding sharding, and it costs about a hundred lines.</para>
/// <para><b>What must not be emitted.</b> <c>useStatefulReconnect</c>. A client that did not ask for
/// it rejects the whole connection with
/// <c>FailedToNegotiateWithServerError("Client didn't negotiate Stateful Reconnect but the server
/// did.")</c> (<c>HttpConnection.ts:358-360</c>). Stateful reconnect is SKIPped, so
/// omitting the field is both correct and free.</para>
/// </remarks>
public static class Negotiate
{
    /// <summary>The one transport this host offers, and the one format it offers on it.</summary>
    public const string Transport = "WebSockets";

    /// <summary>
    /// Builds a negotiate response body.
    /// </summary>
    /// <param name="connectionId">The connection id the client reports and logs.</param>
    /// <param name="connectionToken">
    /// The opaque routing key. Copied from <paramref name="connectionId"/> when null, which is what
    /// the client itself does for <c>negotiateVersion &lt; 1</c> (<c>HttpConnection.ts:352-356</c>).
    /// An app that shards signs its Durable Object name into this instead.
    /// </param>
    public static string Response (string connectionId, string? connectionToken = null) =>
        JsonSerializer.Serialize(
            new NegotiateResponseView(
                NegotiateVersion: 1,
                ConnectionId: connectionId,
                ConnectionToken: connectionToken ?? connectionId,
                AvailableTransports: [new NegotiateTransportView(Transport, ["Text"])]),
            SignalRJsonContext.Default.NegotiateResponseView);

    /// <summary>
    /// A negotiate failure the client aborts on rather than retries
    /// (<c>HttpConnection.ts</c> reads <c>error</c> before anything else).
    /// </summary>
    public static string Error (string message) =>
        JsonSerializer.Serialize(new NegotiateErrorView(message), SignalRJsonContext.Default.NegotiateErrorView);

    /// <summary>
    /// A redirect response: the client follows <c>url</c> (up to 100 hops) carrying
    /// <c>accessToken</c>. The shape an app uses to hand a connection to another Worker or region.
    /// </summary>
    public static string Redirect (string url, string? accessToken = null) =>
        JsonSerializer.Serialize(new NegotiateRedirectView(url, accessToken),
            SignalRJsonContext.Default.NegotiateRedirectView);

    /// <summary>
    /// Whether a request path is the negotiate call for <paramref name="hubPath"/>. The client posts
    /// to <c>{url}/negotiate?negotiateVersion=1</c> (<c>_resolveNegotiateUrl</c>,
    /// <c>HttpConnection.ts:691-716</c>.
    /// </summary>
    public static bool IsNegotiate (string path, string hubPath) =>
        path.Equals(Combine(hubPath, "negotiate"), StringComparison.Ordinal);

    private static string Combine (string hubPath, string segment) =>
        hubPath.EndsWith('/') ? hubPath + segment : $"{hubPath}/{segment}";
}
