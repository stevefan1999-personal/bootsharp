using Microsoft.AspNetCore.SignalR;
using Bootsharp.Cloudflare.SignalR;

namespace Cloudflare.Backend;

/// <summary>
/// The Durable Object hosting <see cref="ChatHub"/>. One class per hub, one instance per room
/// <c>env.CHAT.getByName("lobby")</c> is the room, and "the server process" every
/// <c>DefaultHubLifetimeManager</c> semantic is defined against is this instance.
/// </summary>
/// <remarks>
/// The whole hosting surface is inherited. <c>Accept</c>/<c>Deliver</c>/<c>Disconnect</c>/<c>Sweep</c>
/// come from the base as ordinary Bootsharp RPC methods, because workerd reserves the hibernation
/// handler names (<c>webSocketMessage</c> and friends) and the entrypoint generator refuses to
/// project onto them; the shipped <c>js/signalr.mjs</c> supplies those handlers and forwards to
/// these. What the app writes is the two lines below.
/// </remarks>
public sealed class ChatRoom (IDurableObjectState ctx, ICloudflareEnv env)
    : HubDurableObject<ChatHub, ICloudflareEnv>(ctx, env)
{
    /// <summary>Binds the generated dispatch table for <see cref="ChatHub"/>.</summary>
    protected override HubDispatcher<ChatHub> CreateDispatcher () => new ChatHubDispatcher();

    /// <summary>
    /// The negotiate body for a client about to connect to this room. It is answered on the actor
    /// rather than in the worker because the room IS the routing decision the token carries, which
    /// is argument for shipping a negotiate endpoint at all.
    /// </summary>
    public string NegotiateResponse (string connectionId) => Negotiate.Response(connectionId);
}
