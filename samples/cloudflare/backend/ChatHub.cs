using Microsoft.AspNetCore.SignalR;

namespace Cloudflare.Backend;

/// <summary>
/// A chat hub, written exactly as it would be against ASP.NET Core: derive from <see cref="Hub"/>,
/// take arguments, call <c>Clients</c> and <c>Groups</c>. Nothing on this class knows it is running
/// inside a Durable Object, and a stock <c>@microsoft/signalr</c> client calls it over the real wire
/// protocol.
/// </summary>
/// <remarks>
/// Dispatch is generated, not reflected: <c>Bootsharp.Cloudflare.Generate</c> emits a
/// <c>ChatHubDispatcher</c> carrying a case-insensitive name-to-slot table, one static parameter-type
/// array per method to feed <c>IInvocationBinder</c>, and direct unboxed calls. That is why this hub
/// costs nothing at runtime that a hand-written switch would not, and why an unsupported signature is
/// a compile error rather than a surprise at handshake.
/// </remarks>
public class ChatHub : Hub
{
    /// <summary>
    /// Fans a message out to every connection of this room, the caller included — the base case
    /// two clients can observe at once, and what the sample's <c>/chat</c> page demonstrates.
    /// </summary>
    public Task Send (string user, string message) =>
        Clients.All.SendAsync("receive", user, message);

    /// <summary>
    /// Joins a named group. Membership lives in the socket's hibernation attachment rather than in
    /// isolate state, so it survives the actor being evicted and rebuilt.
    /// </summary>
    public Task Join (string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);

    /// <summary>Sends to one group only; a non-member hears nothing.</summary>
    public Task SendToGroup (string group, string user, string message) =>
        Clients.Group(group).SendAsync("receive", user, message);

    /// <summary>A blocking invocation with a result, which is the other half of the protocol.</summary>
    public string Who () => Context.ConnectionId;

    /// <summary>
    /// The one exception type whose message is part of the contract. Everything else the client is
    /// told about as an opaque failure, so an internal message can never leak to a browser.
    /// </summary>
    public string Refuse () => throw new HubException("refused by contract");
}
