namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// A FIFO of in-flight frames per connection. This is the piece that supplies the ordering workerd
/// does not, and it is not a defensive nicety — it is the only thing standing between a hub method
/// that awaits a binding and out-of-order completions.
/// </summary>
/// <remarks>
/// <para><b>The measurement it exists for</b> (harness: <c>src/js/test/do-interleave</c>, findings
/// in its <c>findings.json</c>). asked for "a harness proving ordered, non-interleaved
/// hub dispatch". That harness proved the opposite:</para>
/// <list type="number">
/// <item>Frames are <i>delivered</i> in send order, but the handler for frame N+1 is <i>entered</i>
/// while the handler for frame N is suspended — on one socket, not merely across sockets.
/// workerd's per-socket hibernation read loop resumes as soon as the handler's first await
/// suspends, because <c>HibernatableWebSocketCustomEvent::run</c> awaits only the synchronous JS
/// turn and hands the handler promise to <c>waitUntil</c>.</item>
/// <item>The interleave point is <b>any non-storage await</b>. Decisive A/B with the same 30 resume
/// points: 30 Durable Object storage reads → no interleaving; 30 host timers → interleaved at
/// resume 9. <c>IoContext::awaitIoWithInputLock</c> holds the actor input gate and
/// <c>api/actor-state.c++</c> is its only caller; everything else releases it. So a hub method
/// awaiting KV/D1/R2/fetch — the exact case names — is unprotected.</item>
/// <item>Completions therefore leave in await-completion order: three frames awaiting 300/200/60 ms
/// arrive 1,2,3 and complete 3,2,1 at depth 3.</item>
/// </list>
/// <para><b>Why per connection and not per Durable Object.</b> Cross-connection concurrency is
/// correct — it is what "one server process, many connections" means — and one queue per actor would
/// park every connection behind one slow hub method. Per connection is also the <i>faithful</i>
/// choice rather than an invention: upstream's <c>MaximumParallelInvocationsPerClient</c> defaults
/// to 1 (<c>HubOptions.cs:68-78</c>), so serializing one connection's frames reproduces upstream
/// semantics exactly.</para>
/// <para><b>What it buys beyond ordering.</b> The handshake ordering becomes free: the first frame's
/// dispatch completes — writing the 3-byte <c>7B 7D 1E</c> — before any second frame is dispatched,
/// so "the first frame the client receives must be the handshake" needs no flag consulted across an
/// await.</para>
/// <para><b>What it deliberately does not do.</b> It does not take bootsharp's <c>reentrant()</c>
/// gate, and that gate must not be changed to serialize: an actor call made from inside a gated
/// .NET call would deadlock. The serialization belongs here, one level up.</para>
/// </remarks>
internal sealed class ConnectionDispatchQueue
{
    private readonly Dictionary<string, Task> tails = new(StringComparer.Ordinal);

    /// <summary>Connections with at least one frame in flight; for diagnostics and tests.</summary>
    public int Depth => tails.Count;

    /// <summary>
    /// Chains <paramref name="work"/> behind whatever is already in flight for this connection and
    /// hands back the chained task. The caller — the JavaScript <c>webSocketMessage</c> handler
    /// awaits it, which is what keeps the event alive across the whole dispatch and gives the
    /// connection backpressure.
    /// </summary>
    /// <remarks>
    /// Everything up to <c>Run</c>'s first await runs inside the caller's synchronous JavaScript
    /// turn, so the tail is replaced before any other frame can be delivered. That is the same
    /// property the whole design leans on: one synchronous turn at a time is the one thing workerd
    /// does guarantee.
    /// </remarks>
    public Task Enqueue (string connectionId, Func<Task> work)
    {
        var previous = tails.TryGetValue(connectionId, out var tail) ? tail : Task.CompletedTask;
        var next = Run(previous, work);
        tails[connectionId] = next;
        return next;
    }

    /// <summary>
    /// Forgets a connection's queue. Called on close: leaving the entry would hold the last frame's
    /// task — and everything it captured — for the life of the isolate.
    /// </summary>
    public void Forget (string connectionId) => tails.Remove(connectionId);

    private static async Task Run (Task previous, Func<Task> work)
    {
        // A frame that faulted must not poison the queue behind it: upstream drops one failed
        // invocation and keeps the connection, and a completion error has already been reported to
        // the client by the dispatcher before it ever reaches here.
        try { await previous.ConfigureAwait(false); }
        catch { /* observed by the caller that enqueued it */ }
        await work().ConfigureAwait(false);
    }
}
