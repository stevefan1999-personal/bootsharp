using Microsoft.AspNetCore.SignalR;

namespace SignalR.Harness;

/// <summary>
/// An ordinary user hub: nothing here is harness-specific, which is the point. It is written
/// exactly as it would be against ASP.NET Core, and the stock <c>@microsoft/signalr</c> npm client
/// calls it over the real wire protocol.
/// </summary>
public class ChatHub : Hub
{
    /// <summary>A blocking invocation with one argument and a result — the base case.</summary>
    public string Echo (string message) => message;

    /// <summary>Two arguments, bound to their declared types by the generated parameter table.</summary>
    public int Add (int left, int right) => left + right;

    /// <summary>Fans out to every connection of this room, including the caller.</summary>
    public Task Broadcast (string user, string message) => Clients.All.SendAsync("receive", user, message);

    /// <summary>Group membership, which lives in the socket's hibernation attachment.</summary>
    public Task Join (string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);

    public Task ToGroup (string group, string message) =>
        Clients.Group(group).SendAsync("receive", "group", message);

    /// <summary>The caller's connection id, which is the socket's immutable first accept tag.</summary>
    public string Who () => Context.ConnectionId;

    /// <summary>
    /// Counts invocations in a C# static. A hibernation wake rebuilds the Durable Object but keeps
    /// the isolate (do-interleave finding 6), so this is how the driver tells "the connection
    /// survived" apart from "the whole worker restarted".
    /// </summary>
    public int Count () => ++calls;

    private static int calls;

    /// <summary>An error whose message is a contract, unlike every other exception.</summary>
    public string Refuse () => throw new HubException("refused by contract");
}
