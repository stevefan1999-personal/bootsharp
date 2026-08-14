using System.Text.Json;

namespace Interleave.Harness;

/// <summary>
/// A hub-method-shaped handler with every interleaving point instrumented. Every observation the
/// harness makes — from JavaScript as well as from C# — lands in one monotonically numbered trace,
/// so "did these two events interleave" is answered by reading step numbers rather than by
/// comparing clocks.
/// </summary>
/// <remarks>
/// <para>A <c>program</c> is <c>label|op,op,…</c>. Each op is one await the handler performs
/// between two trace entries:</para>
/// <list type="bullet">
/// <item><c>dl&lt;ms&gt;</c> — <c>Task.Delay</c>, i.e. a host timer. Non-storage I/O.</item>
/// <item><c>kv&lt;n&gt;</c> — n awaited <c>env.KV.Get</c> calls: the "binding call" of
/// Non-storage I/O — workerd awaits it through <c>IoContext::awaitIo</c>, which does not hold
/// the actor input gate.</item>
/// <item><c>st&lt;n&gt;</c> — n awaited <c>ctx.storage.get</c> calls, through one hoisted handle.
/// Durable Object storage is the one API workerd awaits through <c>awaitIoWithInputLock</c>, so
/// the input gate IS held.</item>
/// <item><c>sp&lt;n&gt;</c> — the same reads, re-reading the <c>Ctx.Storage</c> handle property on
/// every iteration. Separated from <c>st</c> because that difference alone decides whether the
/// handle survives.</item>
/// <item><c>yl&lt;n&gt;</c> — n <c>Task.Yield()</c>s: a continuation that stays on the microtask
/// queue and therefore inside the same JS turn.</item>
/// <item><c>sy</c> — <c>ctx.storage.sync()</c>.</item>
/// </list>
/// <para>State is kept per Durable Object <em>scope name</em>, not per actor incarnation and not
/// per isolate. Per scope is what makes each scenario independent while still surviving a
/// hibernation wake, which rebuilds the actor under the same name — the exact distinction
/// scenario 3 measures. Statics are the isolate, so <see cref="isolateBornAt"/> separates "the
/// actor came back" from "the isolate came back", and a wake that kept the isolate is a wake that
/// paid no.NET boot.</para>
/// </remarks>
public sealed class Hub : IHub
{
    private readonly record struct Entry(int Step, long At, string Event, string Detail, int Depth);

    /// <summary>Everything one Durable Object scope observes, across all of its incarnations.</summary>
    private sealed class Scope
    {
        public readonly List<Entry> Trace = [];
        public readonly Dictionary<string, int> Conversations = [];
        public int Step;
        public int Depth;
        public int PeakDepth;
        public int Constructions;
    }

    private sealed record Actor(Scope Scope, IDurableObjectState Ctx, IHarnessEnv Env);

    private static readonly Dictionary<string, Scope> scopes = [];
    private static readonly Dictionary<int, Actor> actors = [];
    private static readonly DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly long isolateBornAt = Now();
    private static int nextActor = 1;

    public int Construct (string scope, IDurableObjectState ctx, IHarnessEnv env)
    {
        if (!scopes.TryGetValue(scope, out var state)) scopes[scope] = state = new Scope();
        var id = nextActor++;
        actors[id] = new Actor(state, ctx, env);
        state.Constructions++;
        Record(state, "cs:construct", $"scope={scope} actor={id}");
        return id;
    }

    public async Task<string> Message (int actor, string connection, string program)
    {
        var state = Resolve(actor);
        var (label, ops) = Parse(program);
        var turn = Bump(state, connection);
        state.Depth++;
        if (state.Depth > state.PeakDepth) state.PeakDepth = state.Depth;
        Record(state, "cs:enter", $"{label} conn={connection} turn={turn} ops={ops.Length}");
        try
        {
            for (var index = 0; index < ops.Length; index++)
            {
                var detail = await Run(actor, ops[index]);
                Record(state, $"cs:resume{index + 1}", $"{label} {ops[index]}{detail}");
            }
        }
        // Recorded rather than propagated: a handler that dies mid-await would otherwise leave the
        // depth counter wrong for the rest of the scenario, and the reason would be lost inside the
        // generic "C# exception from NativeAOT" the interop boundary reports.
        catch (Exception error) { Record(state, "cs:throw", $"{label} {error.GetType().Name}: {error.Message}"); }
        finally { state.Depth--; }
        Record(state, "cs:exit", $"{label} conn={connection} turn={turn}");
        return label;
    }

    public async Task<string> Alarm (int actor, string program)
    {
        var state = Resolve(actor);
        var (label, ops) = Parse(program);
        state.Depth++;
        if (state.Depth > state.PeakDepth) state.PeakDepth = state.Depth;
        Record(state, "cs:alarm-enter", label);
        try { foreach (var op in ops) Record(state, "cs:alarm-resume", $"{label} {op}{await Run(actor, op)}"); }
        catch (Exception error) { Record(state, "cs:alarm-throw", $"{label} {error.GetType().Name}: {error.Message}"); }
        finally { state.Depth--; }
        Record(state, "cs:alarm-exit", label);
        return label;
    }

    public Task<string> Closed (int actor, string connection, int code)
    {
        Record(Resolve(actor), "cs:close", $"conn={connection} code={code}");
        return Task.FromResult(connection);
    }

    public void Note (int actor, string @event, string detail) => Record(Resolve(actor), @event, detail);

    public void Reset (int actor)
    {
        var state = Resolve(actor);
        state.Trace.Clear();
        state.Step = 0;
        state.PeakDepth = 0;
        state.Depth = 0;
    }

    public string Report (int actor)
    {
        var state = Resolve(actor);
        return JsonSerializer.Serialize(
            new InterleaveReport(
                isolateBornAt,
                state.Constructions,
                actor,
                state.PeakDepth,
                state.Depth,
                ".NET " + Environment.Version,
                state.Conversations,
                [.. state.Trace.Select(entry => new TraceEntry(entry.Step, entry.At, entry.Event, entry.Detail, entry.Depth))]),
            ApiJsonContext.Default.InterleaveReport);
    }

    private static Scope Resolve (int actor) =>
        actors.TryGetValue(actor, out var found)
            ? found.Scope
            : throw new InvalidOperationException($"Actor {actor} was never constructed.");

    /// <summary>Per-connection turn counter: a "conversation" that a hibernation wake must not lose.</summary>
    private static int Bump (Scope state, string connection)
    {
        var turn = (state.Conversations.TryGetValue(connection, out var previous) ? previous : 0) + 1;
        state.Conversations[connection] = turn;
        return turn;
    }

    private static async Task<string> Run (int actor, string op)
    {
        var state = actors[actor];
        var (kind, count) = Split(op);
        switch (kind)
        {
            case "dl":
                await Task.Delay(count);
                return "";
            case "kv":
                for (var index = 0; index < count; index++) await state.Env.KV.Get("interleave");
                return "";
            case "st":
            {
                // The handle is read out of the property once. See "sp" for why that matters.
                var storage = state.Ctx.Storage;
                for (var index = 0; index < count; index++) await storage.Get("interleave");
                return "";
            }
            case "sp":
                // Same reads, but re-reading the handle property on every iteration — the half of
                // the A/B that used to intermittently die with
                // "TypeError: Cannot read properties of undefined (reading 'get')" (2 of 9 runs,
                // against 0 of 9 for the hoisted path above). It no longer fails (0 of 25) — but
                // neither does the pre-fix build when re-measured (also 0 of 25), so that is a
                // no-regression result, not a demonstration. The earlier hypothesis recorded here
                // — several C# proxies racing to dispose one
                // shared id — is refuted: Instances.Resolve caches weakly BY ID, so there is only
                // ever one proxy and one finalizer per id. The two real causes were a registry that
                // did not refcount its hand-offs, and a proxy the precise GC could finalize between
                // the read of _id and the interop call carrying it. See the README A/B table and
                // Kept as a standing regression guard, because a hub's lifetime
                // manager reaches through Ctx.Storage exactly this way.
                for (var index = 0; index < count; index++) await state.Ctx.Storage.Get("interleave");
                return "";
            case "yl":
                for (var index = 0; index < count; index++) await Task.Yield();
                return "";
            case "sy":
                await state.Ctx.Storage.Sync();
                return "";
            default:
                return " unknown-op";
        }
    }

    private static (string Label, string[] Ops) Parse (string program)
    {
        var bar = program.IndexOf('|');
        if (bar < 0) return (program, []);
        var ops = program.Substring(bar + 1);
        return (program.Substring(0, bar), ops.Length == 0 ? [] : ops.Split(','));
    }

    private static (string Kind, int Count) Split (string op)
    {
        var index = 0;
        while (index < op.Length && !char.IsDigit(op[index])) index++;
        var kind = op.Substring(0, index);
        var count = index < op.Length && int.TryParse(op.Substring(index), out var parsed) ? parsed : 1;
        return (kind, count);
    }

    private static void Record (Scope state, string @event, string detail) =>
        state.Trace.Add(new Entry(++state.Step, Now(), @event, detail, state.Depth));

    private static long Now () => (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
}
