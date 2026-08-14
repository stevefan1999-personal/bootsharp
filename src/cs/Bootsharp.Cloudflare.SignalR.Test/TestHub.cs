using Microsoft.AspNetCore.SignalR;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// What a dispatch records, owned by one test rather than by the type. Static recording would be
/// simpler and wrong: xUnit runs test classes in parallel, so two suites would clear each other's
/// trace mid-run and the ordering assertions — the ones this package exists for — would flake.
/// </summary>
internal sealed class HubRecorder
{
    /// <summary>Hub entries and exits, in the order they happened.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Gates a dispatch on a task the test completes, which is how ordering is asserted.</summary>
    public Dictionary<string, TaskCompletionSource> Gates { get; } = [];

    public TaskCompletionSource Gate (string key) => Gates[key] = new TaskCompletionSource();
}

/// <summary>
/// The hub every runtime test dispatches into. Its methods cover the shapes the handler treats
/// differently: void, value-returning, awaited, renamed on the wire, throwing a
/// <see cref="HubException"/> (whose message is a contract), throwing anything else (whose message
/// is not), and one whose completion order is controlled by the test.
/// </summary>
internal sealed class TestHub (HubRecorder recorder) : Hub
{
    public string Echo (string message) => message;

    public int Add (int left, int right) => left + right;

    public void Notify (string message) => recorder.Calls.Add($"notify:{message}");

    [HubMethodName("client.ping")]
    public string Renamed () => "pong";

    public async Task<string> Gated (string key)
    {
        recorder.Calls.Add($"enter:{key}");
        // A non-storage await is precisely the point workerd releases the actor input gate and lets
        // the next frame in (src/js/test/do-interleave finding 3). The FIFO is what has to stop it.
        await recorder.Gates[key].Task.ConfigureAwait(false);
        recorder.Calls.Add($"exit:{key}");
        return key;
    }

    public Task Join (string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);

    public Task Leave (string group) => Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

    public Task Broadcast (string message) => Clients.All.SendAsync("receive", message);

    public string Refuse () => throw new HubException("refused by contract");

    public string Explode () => throw new InvalidOperationException("internal detail");

    public string Whoami () => Context.ConnectionId;
}

/// <summary>
/// <see cref="TestHub"/>'s dispatch, hand-written in the shape <c>HubEmitter</c> emits: a
/// case-insensitive name→slot switch honouring <c>[HubMethodName]</c>, one static parameter-type
/// array per method, and a switch of direct, unboxed calls.
/// </summary>
/// <remarks>
/// Deliberately not produced by running the generator here. These are runtime tests; a generator
/// regression should fail <c>Bootsharp.Cloudflare.Generate.Test</c>'s <c>HubDispatchTests</c>, not
/// silently disable every assertion in this suite.
/// </remarks>
internal sealed class TestHubDispatcher (HubRecorder recorder) : HubDispatcher<TestHub>
{
    private static readonly Type[] noParameters = [];
    private static readonly Type[] oneString = [typeof(string)];
    private static readonly Type[] twoInts = [typeof(int), typeof(int)];

    public override int Slot (string methodName) => methodName switch {
        var name when string.Equals(name, "Echo", StringComparison.OrdinalIgnoreCase) => 0,
        var name when string.Equals(name, "Add", StringComparison.OrdinalIgnoreCase) => 1,
        var name when string.Equals(name, "Notify", StringComparison.OrdinalIgnoreCase) => 2,
        var name when string.Equals(name, "client.ping", StringComparison.OrdinalIgnoreCase) => 3,
        var name when string.Equals(name, "Gated", StringComparison.OrdinalIgnoreCase) => 4,
        var name when string.Equals(name, "Join", StringComparison.OrdinalIgnoreCase) => 5,
        var name when string.Equals(name, "Leave", StringComparison.OrdinalIgnoreCase) => 6,
        var name when string.Equals(name, "Broadcast", StringComparison.OrdinalIgnoreCase) => 7,
        var name when string.Equals(name, "Refuse", StringComparison.OrdinalIgnoreCase) => 8,
        var name when string.Equals(name, "Explode", StringComparison.OrdinalIgnoreCase) => 9,
        var name when string.Equals(name, "Whoami", StringComparison.OrdinalIgnoreCase) => 10,
        _ => -1
    };

    public override IReadOnlyList<Type> ParameterTypes (int slot) => slot switch {
        0 or 2 or 4 or 5 or 6 or 7 => oneString,
        1 => twoInts,
        _ => noParameters
    };

    public override async ValueTask<object?> Invoke (int slot, TestHub hub, object?[] args)
    {
        switch (slot)
        {
            case 0: return hub.Echo((string)args[0]!);
            case 1: return hub.Add((int)args[0]!, (int)args[1]!);
            case 2:
                hub.Notify((string)args[0]!);
                return null;
            case 3: return hub.Renamed();
            case 4: return await hub.Gated((string)args[0]!);
            case 5:
                await hub.Join((string)args[0]!);
                return null;
            case 6:
                await hub.Leave((string)args[0]!);
                return null;
            case 7:
                await hub.Broadcast((string)args[0]!);
                return null;
            case 8: return hub.Refuse();
            case 9: return hub.Explode();
            case 10: return hub.Whoami();
            default:
                await Task.CompletedTask;
                throw new InvalidOperationException($"Hub method slot {slot} does not exist.");
        }
    }

    public override TestHub Create () => new(recorder);

    public override IReadOnlyList<string> MethodNames { get; } =
        ["Echo", "Add", "Notify", "client.ping", "Gated", "Join", "Leave", "Broadcast", "Refuse", "Explode", "Whoami"];
}
