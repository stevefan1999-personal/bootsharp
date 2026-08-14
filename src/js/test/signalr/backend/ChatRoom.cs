using Microsoft.AspNetCore.SignalR;
using Bootsharp.Cloudflare.SignalR;

namespace SignalR.Harness;

/// <summary>
/// One Durable Object class per hub, one instance per room. Everything the hosting needs is
/// inherited: <c>Accept</c>/<c>Deliver</c>/<c>Disconnect</c>/<c>Sweep</c> come from the base and are
/// injected into both projections by the shared rules, and the hibernation handlers that forward to
/// them are the shipped <c>js/signalr.mjs</c>.
/// </summary>
public sealed class ChatRoom (IDurableObjectState ctx, IHarnessEnv env)
    : HubDurableObject<ChatHub, IHarnessEnv>(ctx, env)
{
    protected override HubDispatcher<ChatHub> CreateDispatcher () => new ChatHubDispatcher();

    /// <summary>
    /// The negotiate body for a client about to connect to this room. It lives on the actor rather
    /// than in the worker because the room IS the routing decision — which is the argument
    /// §4 makes for shipping negotiate at all.
    /// </summary>
    public string NegotiateResponse (string connectionId) => Negotiate.Response(connectionId);
}
