using System.Text.Json;

namespace Llvm.Exercise;

/// <summary>
/// The whole exercise: one route that reports, as JSON, what the three milestone-0b capabilities
/// actually did on this invocation. Nothing is asserted here — the runner compares the reports of
/// two consecutive invocations, because every property under test is about what survives an
/// invocation boundary and what does not.
/// </summary>
public sealed class Worker : WorkerEntrypoint<IExerciseEnv, ExerciseResponse>, IWorker
{
    // Held across invocations on purpose. Static state is isolate state here: workerd keeps the
    // isolate alive between requests, which is exactly the lifetime the handle scopes are about.
    private static IKvNamespace? heldIsolateHandle;
    private static IProbeStub? heldInvocationHandle;
    private static int invocations;

    public override async Task<ExerciseResponse> Fetch (IJsRequest request, IExerciseEnv env)
    {
        // Yield first, so the rest of the handler runs off the JS call that entered wasm.
        await Task.Yield();
        WorkerContext.Set(env);
        try
        {
            var url = new Uri(request.Url, UriKind.Absolute);
            if (url.AbsolutePath != "/probe") return Reply(404, Write(new ErrorView("not found")));
            return Reply(200, await Probe());
        }
        catch (Exception error) { return Reply(500, Write(new ErrorView(error.ToString()))); }
        finally { WorkerContext.Clear(); }
    }

    private static async Task<string> Probe ()
    {
        var invocation = ++invocations;
        var env = WorkerContext.Env;

        // (a) Awaited primitive import. The stub member is Task<int> behind a workerd JsRpcPromise;
        // it marshals only because the generated import awaits every async member. This is also
        // the actor's second construction-free RPC on invocation 2, so a released IDurableObjectState
        // handle would surface right here.
        var freshStub = env.PROBE.GetByName("probe");
        var awaitedInt = await freshStub.Increment();

        // (b) Handle identity. The env wrapper memoizes the KV adapter for the isolate and
        // [JSHandle(Scope = Isolate)] keeps it out of the invocation's disposal set, so the same
        // JavaScript object must resolve to the same C# proxy on every invocation — and calling
        // through the proxy held since invocation 1 must still reach the live binding.
        var currentIsolateHandle = env.KV;
        var isolateSameProxy = heldIsolateHandle is not null && ReferenceEquals(heldIsolateHandle, currentIsolateHandle);
        var isolateUsable = false;
        string? isolateError = null;
        if (heldIsolateHandle is not null)
            try { await heldIsolateHandle.Put("probe", invocation.ToString(), null); isolateUsable = true; }
            catch (Exception error) { isolateError = error.Message; }
        heldIsolateHandle = currentIsolateHandle;

        // (c) Disposal scope. The stub is a fresh JavaScript object per call and carries no isolate
        // scope, so the invocation that imported it releases it when it ends. Holding one past that
        // point must therefore fail: the JS registry no longer resolves its id.
        var invocationSameProxy = heldInvocationHandle is not null && ReferenceEquals(heldInvocationHandle, freshStub);
        var staleThrew = false;
        string? staleError = null;
        if (heldInvocationHandle is not null)
            try { await heldInvocationHandle.Increment(); }
            catch (Exception error) { staleThrew = true; staleError = error.Message; }
        heldInvocationHandle = freshStub;

        return Write(new ProbeReport(
            invocation,
            awaitedInt,
            awaitedInt.GetType().Name,
            heldIsolateHandle is not null,
            isolateSameProxy,
            isolateUsable,
            isolateError,
            invocationSameProxy,
            staleThrew,
            staleError,
            ".NET " + Environment.Version));
    }

    private static string Write (ErrorView value) =>
        JsonSerializer.Serialize(value, ApiJsonContext.Default.ErrorView);

    private static string Write (ProbeReport value) =>
        JsonSerializer.Serialize(value, ApiJsonContext.Default.ProbeReport);

    private static ExerciseResponse Reply (int status, string body) =>
        new(status, """{"content-type":"application/json; charset=utf-8"}""", body);
}
