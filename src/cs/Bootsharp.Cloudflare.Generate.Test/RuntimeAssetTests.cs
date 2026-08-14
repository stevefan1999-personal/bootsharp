using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// milestone 2: everything that is the same for every app lives in the packaged
/// <c>js/runtime.mjs</c> asset, and the emitted module imports it instead of carrying a copy.
/// These tests hold that seam — the emitted module must import exactly what it calls, and the
/// asset must still export it.
/// </summary>
public class RuntimeAssetTests
{
    [Fact]
    public void EmittedModuleImportsTheRuntimeAssetRatherThanDeclaringIt ()
    {
        var run = GeneratorHarness.RunActors(TestSources.FetchOnlyWorker, TestSources.CounterActor);
        Assert.Contains("} from \"./runtime.mjs\";", run.GeneratedJs);
        // The wasm module and the Bootsharp entry are the app's, so they are the only two paths
        // the module names; the publish task computes both from where the build put them.
        Assert.Contains("import wasmModule from \"../wasm/backend.wasm\";", run.GeneratedJs);
        Assert.Contains("configureRuntime(wasmModule, () => import(\"../js/index.mjs\"));", run.GeneratedJs);
        // The wasm gate, the boot gate and the binding adapters are the asset's body, not emitted.
        Assert.DoesNotContain("WebAssembly", run.GeneratedJs);
        Assert.DoesNotContain("function ensureBoot", run.GeneratedJs);
        Assert.DoesNotContain("function wrapIKvNamespace", run.GeneratedJs);
    }

    /// <summary>
    /// The import list is projected, like everything else: a worker that binds nothing and hosts no
    /// actor must not name the actor or cron helpers at all.
    /// </summary>
    [Fact]
    public void OnlyTheHelpersTheProjectionActuallyCallsAreImported ()
    {
        var lean = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Contains("wrapRequest", lean.RuntimeImports);
        Assert.DoesNotContain("wrapState", lean.RuntimeImports);
        Assert.DoesNotContain("wrapStep", lean.RuntimeImports);
        Assert.DoesNotContain("wrapScheduledController", lean.RuntimeImports);
        var full = GeneratorHarness.RunActors(TestSources.ScheduledWorker, TestSources.CounterActor, TestSources.DemoWorkflow);
        Assert.Contains("wrapState", full.RuntimeImports);
        Assert.Contains("wrapStep", full.RuntimeImports);
        Assert.Contains("wrapScheduledController", full.RuntimeImports);
    }

    /// <summary>
    /// every handle imported during an invocation is released when it ends, so the
    /// isolate-lived env wrapper has to be exempted where it is memoized. The app's env interface is
    /// the app's, so it cannot carry <c>[JSHandle(Scope = Isolate)]</c> and the emitted
    /// wrapper says it imperatively instead — one line, before the memo, on the only path that
    /// builds the wrapper.
    /// </summary>
    [Fact]
    public void TheMemoizedEnvWrapperIsExemptedFromInvocationScoping ()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Contains("  exemptHandle(wrapped);\n  wrappedEnvs.set(env, wrapped);", run.GeneratedJs);
        Assert.Contains("exemptHandle", run.RuntimeImports);
    }

    /// <summary>
    /// The <c>RpcInt</c> box existed only because generated JS did not await a <c>Task&lt;int&gt;</c>
    /// instance import, so a workerd <c>JsRpcPromise</c> reached the int marshaler unresolved
    ///. Now that every async import is awaited, the stub passes the RPC
    /// result through unchanged and neither the box nor its JS normaliser is emitted.
    /// </summary>
    [Fact]
    public void DurableObjectStubsPassRpcResultsThroughWithoutNumberNormalisation ()
    {
        var run = GeneratorHarness.RunActors(TestSources.FetchOnlyWorker, TestSources.CounterActor);
        // `total` returns int and `add` returns Task<int> — both used to be normalised by rpcNumber.
        Assert.Contains("wrap.total = (...args) => stub[\"total\"](...args);", run.GeneratedJs);
        Assert.Contains("wrap.add = (...args) => stub[\"add\"](...args);", run.GeneratedJs);
        Assert.DoesNotContain("rpcNumber", run.GeneratedJs);
        Assert.DoesNotContain("rpcNumber", GeneratorHarness.PackagedJsRuntime);
    }

    /// <summary>
    /// The one binding the C# side cannot check: the asset is JavaScript, so an import of a helper
    /// it stopped exporting fails in workerd rather than in the build.
    /// </summary>
    [Fact]
    public void EveryImportedHelperIsStillExportedByThePackagedAsset ()
    {
        var run = GeneratorHarness.RunActors(TestSources.ScheduledWorker, TestSources.CounterActor, TestSources.DemoWorkflow);
        var asset = GeneratorHarness.PackagedJsRuntime;
        Assert.NotEmpty(run.RuntimeImports);
        foreach (var name in run.RuntimeImports)
            Assert.True(
                asset.Contains($"export function {name} (")
                || asset.Contains($"export async function {name} (")
                || asset.Contains($"export {{ {name} }};"),
                $"js/runtime.mjs no longer exports '{name}', which the emitted module imports.");
    }

    /// <summary>
    /// and §4: workerd classifies an entrypoint by its base class, so the module may
    /// only import a base it actually extends — importing an unused one from cloudflare:workers
    /// would advertise a capability the app does not have.
    /// </summary>
    [Fact]
    public void OnlyTheWorkerdBaseClassesTheAppExtendsAreImported ()
    {
        var lean = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Contains("import { WorkerEntrypoint } from \"cloudflare:workers\";", lean.GeneratedJs);
        var full = GeneratorHarness.RunActors(TestSources.FetchOnlyWorker, TestSources.CounterActor, TestSources.DemoWorkflow);
        Assert.Contains(
            "import { DurableObject, WorkerEntrypoint, WorkflowEntrypoint } from \"cloudflare:workers\";",
            full.GeneratedJs);
    }
}
