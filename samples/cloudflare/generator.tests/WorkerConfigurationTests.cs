using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// milestone 4: nothing about one app is written into the library. Everything that used to
/// be a literal in the emitter or the runtime asset — the asset routes, the assets binding, the name
/// of the env interface, the name of every binding in a failure — is declared by the app and
/// projected. These tests are what stops a second app's values creeping back in.
/// </summary>
public class WorkerConfigurationTests
{
    /// <summary>
    /// The predicate lives once, in the packaged runtime, and takes the app's prefixes as data. The
    /// app declares them once, in an attribute; nothing else may hold a second copy.
    /// </summary>
    [Fact]
    public void AssetRoutesComeFromTheAppsDeclarationAndAreTestedByThePackagedPredicate ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(attributes: """[assembly: WorkerAssets("/app", "/favicon.ico")]"""),
            TestSources.FetchOnlyWorker);
        Assert.Empty(run.DefectIds);
        Assert.Contains("""const assetPaths = ["/app", "/favicon.ico"];""", run.GeneratedJs);
        Assert.Contains("if (isAssetPath(url.pathname, assetPaths) && this.env.ASSETS)", run.GeneratedJs);
        // The predicate is imported, so the module carries no route list of its own beyond the data.
        Assert.Contains("isAssetPath", run.RuntimeImports);
        Assert.DoesNotContain("startsWith", run.GeneratedJs);
    }

    /// <summary>
    /// An app that serves no assets declares nothing, and then the emitted module mentions no
    /// binding at all — neither the pre-boot fast path (there is no route list to guess) nor the
    /// post-guest fallback. The fallback is the load-bearing half: wrangler projects <c>Env</c>
    /// from the bindings it configures, so a module reading <c>this.env.ASSETS</c> out of a worker
    /// that has no assets binding fails the app's own tsc gate.
    /// </summary>
    [Fact]
    public void WithoutADeclarationTheModuleNamesNoAssetsBinding ()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.DoesNotContain("assetPaths", run.GeneratedJs);
        Assert.DoesNotContain("isAssetPath", run.RuntimeImports);
        Assert.DoesNotContain("ASSETS", run.GeneratedJs);
        // A request the guest declines is then simply not this worker's.
        Assert.Contains("""return new Response("Not found", { status: 404 });""", run.GeneratedJs);
    }

    /// <summary>
    /// Declaring the attribute with no prefixes is how an app says "I have an assets binding, but
    /// serve every request through the guest": the fallback is emitted, the fast path is not.
    /// </summary>
    [Fact]
    public void DeclaringAssetsWithoutPrefixesKeepsTheFallbackAndDropsTheFastPath ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(attributes: "[assembly: WorkerAssets]"),
            TestSources.FetchOnlyWorker);
        Assert.Empty(run.DefectIds);
        Assert.DoesNotContain("assetPaths", run.GeneratedJs);
        Assert.Contains("if (this.env.ASSETS) return this.env.ASSETS.fetch(request);", run.GeneratedJs);
    }

    /// <summary>The binding name is wrangler's <c>assets.binding</c>, not a constant of ours.</summary>
    [Fact]
    public void AssetsBindingNameIsTheOneTheAppConfigured ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(attributes: """[assembly: WorkerAssets("/static", Binding = "SITE")]"""),
            TestSources.FetchOnlyWorker);
        Assert.Contains("if (isAssetPath(url.pathname, assetPaths) && this.env.SITE)", run.GeneratedJs);
        Assert.Contains("if (this.env.SITE) return this.env.SITE.fetch(request);", run.GeneratedJs);
        Assert.DoesNotContain("this.env.ASSETS", run.GeneratedJs);
    }

    /// <summary>
    /// The env interface is found by its marker, so an app may call it anything. Its bindings are
    /// named in the emitted adapters, so a missing binding reports the app's own name for it.
    /// </summary>
    [Fact]
    public void EnvInterfaceIsFoundByItsMarkerAndBindingNamesTravelToTheRuntime ()
    {
        var run = GeneratorHarness.RunApp(TestSources.AppWithCounter,
            TestSources.FetchOnlyWorker, TestSources.CounterActor);
        Assert.Empty(run.DefectIds);
        Assert.Contains("""KV: wrapIKvNamespace(env.KV, "KV"),""", run.GeneratedJs);
        Assert.Contains("""COUNTER: wrapICounterNamespace(env.COUNTER, "COUNTER"),""", run.GeneratedJs);
        Assert.Contains("if (!ns) missing(binding);", run.GeneratedJs);
    }

    /// <summary>
    /// adapters are chosen on full type identity. An app interface that happens to share
    /// a Cloudflare binding's name is the app's own handle and must be passed through untouched.
    /// </summary>
    [Fact]
    public void AppInterfaceSharingACloudflareBindingNameIsNotAdaptedAsOne ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(bindings: "IQueue TASKS { get; }", extra: "public interface IQueue { }"),
            TestSources.FetchOnlyWorker);
        Assert.Contains("TASKS: wrapIdentity(env.TASKS),", run.GeneratedJs);
        Assert.DoesNotContain("wrapIQueue(env.TASKS", run.GeneratedJs);
        Assert.Equal(["CFW018"], run.DefectIds);
    }

    /// <summary>
    /// A handle with no adapter still works when the guest can import its JS shape directly, so it
    /// is a warning — but it is also exactly what a renamed Durable Object looks like, which is why
    /// it cannot stay silent.
    /// </summary>
    [Fact]
    public void BindingWithNoAdapterIsReportedWithTheClassItWasLookingFor ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(bindings: "ILedgerNamespace LEDGER { get; }", extra: "public interface ILedgerNamespace { }"),
            TestSources.FetchOnlyWorker);
        Assert.Equal(["CFW018"], run.DefectIds);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, run.Defects.Single().Severity);
        Assert.Contains("'Ledger'", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
    }

    /// <summary>
    /// The runtime binds its log handler to the guest member the app's import produced. Projecting
    /// the name is what lets an app that imports no sink boot at all.
    /// </summary>
    [Fact]
    public void LogSinkIsBoundOnlyWhenTheAppImportsIt ()
    {
        var quiet = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Contains("""configureRuntime(wasmModule, () => import("../js/index.mjs"));""", quiet.GeneratedJs);
        var logging = GeneratorHarness.RunApp(
            TestSources.App(attributes: "[assembly: Import(typeof(ILogSink))]"),
            TestSources.FetchOnlyWorker);
        Assert.Empty(logging.DefectIds);
        Assert.Contains("""configureRuntime(wasmModule, () => import("../js/index.mjs"), "ILogSink");""", logging.GeneratedJs);
    }

    /// <summary>
    /// Using the logging surface without importing the sink used to boot into a TypeError, because
    /// the handler was bound to a member the guest never generated.
    /// </summary>
    [Fact]
    public void UsingTheLoggerWithoutImportingTheSinkIsDiagnosed ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(extra: "public sealed class Logs { public CloudflareJsonLoggerProvider? Provider { get; set; } }"),
            TestSources.FetchOnlyWorker);
        Assert.Equal(["CFW015"], run.DefectIds);
        Assert.Contains("Import(typeof(Bootsharp.Cloudflare.Logging.ILogSink))", run.Defects.Single().GetMessage());
    }

    /// <summary>An actor is constructed against the env, so there has to be exactly one.</summary>
    [Fact]
    public void ActorWithoutAMarkedEnvIsDiagnosedRatherThanEmittedAsUncompilableDispatch ()
    {
        var run = GeneratorHarness.RunApp(TestSources.App(marker: ""), TestSources.CounterActor);
        Assert.Equal(["CFW016"], run.DefectIds);
        Assert.Contains("Counter", run.Defects.Single().GetMessage());
        Assert.Empty(run.GeneratedCs);
    }

    [Fact]
    public void SecondMarkedEnvIsDiagnosedAndTheFirstIsUsed ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(
                extra: "[WorkerEnv] public interface IOtherEnv { IKvNamespace KV { get; } }",
                actorRuntime: TestSources.ActorRuntime),
            TestSources.CounterActor);
        Assert.Equal(["CFW017"], run.DefectIds);
        Assert.Contains("IOtherEnv", run.Defects.Single().GetMessage());
        // Resolution is ordinal, so which one wins does not depend on file or member order.
        Assert.Contains("Cloudflare.Backend.ICloudflareEnv env)", run.GeneratedCs);
    }
}
