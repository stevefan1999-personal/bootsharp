using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The ESM module is projected from the members a worker subclass declares: a handler
/// exists in JS when, and only when, its C# counterpart does.
/// </summary>
public class WorkerEmissionTests
{
    [Fact]
    public void WorkerServingOnlyFetchGetsTheDefaultExportAndNoOtherHandler()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Empty(run.DefectIds);
        Assert.Contains("export default class Site extends WorkerEntrypoint", run.GeneratedJs);
        Assert.Contains("async fetch(request)", run.GeneratedJs);
        Assert.Contains("IWorker.fetch(wrapRequest(request), wrapEnv(this.env))", run.GeneratedJs);
        Assert.DoesNotContain("async scheduled(", run.GeneratedJs);
        Assert.DoesNotContain("async queue(", run.GeneratedJs);
    }

    [Fact]
    public void WorkerOverridingScheduledGetsACronHandlerAndItsController()
    {
        var run = GeneratorHarness.Run(TestSources.ScheduledWorker);
        Assert.Empty(run.DefectIds);
        Assert.Contains("async scheduled(controller)", run.GeneratedJs);
        Assert.Contains("IWorker.scheduled(wrapScheduledController(controller), wrapEnv(this.env))", run.GeneratedJs);
        // The controller adapter is the same for every app, so it is imported from the packaged
        // runtime asset rather than emitted.
        Assert.Contains("wrapScheduledController", run.RuntimeImports);
    }

    /// <summary>
    /// an env property is adapted by its declared interface type. The KV handle uses the
    /// template adapter; the actor namespace uses the one projected from the Durable Object class.
    /// </summary>
    [Fact]
    public void EnvAdaptersAreChosenByPropertyTypeRatherThanBindingName()
    {
        var run = GeneratorHarness.RunApp(TestSources.AppWithCounter,
            TestSources.FetchOnlyWorker, TestSources.CounterActor);
        Assert.Contains("""KV: wrapIKvNamespace(env.KV, "KV"),""", run.GeneratedJs);
        Assert.Contains("""COUNTER: wrapICounterNamespace(env.COUNTER, "COUNTER"),""", run.GeneratedJs);
        Assert.Contains("function wrapICounterNamespace(ns, binding)", run.GeneratedJs);
    }

    /// <summary>
    /// The emitted module is one JavaScript scope shared with the runtime import list, so a C# name
    /// that projects onto a name already there is refused rather than emitted as a redeclaration
    /// nothing but the bundler would catch.
    /// </summary>
    [Fact]
    public void EntrypointProjectingANameTheRuntimeAlreadyDeclaresIsDiagnosed()
    {
        var run = GeneratorHarness.RunActors(TestSources.FetchOnlyWorker, TestSources.CollidingActor);
        Assert.Equal(["CFW019"], run.DefectIds);
        Assert.Contains("wrapIKvNamespace", run.Defects.Single().GetMessage());
    }

    [Fact]
    public void TwoEntrypointsExportingTheSameClassNameAreDiagnosed()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker, TestSources.DuplicateWorker);
        Assert.Equal(["CFW019"], run.DefectIds);
        Assert.Contains("'Site'", run.Defects.Single().GetMessage());
    }

    /// <summary>
    /// The generated dispatch is one half of a partial class whose other half — the one deriving
    /// from the packaged <c>ActorRuntimeBase</c> — an app declares only when it hosts a Durable
    /// Object or a workflow. A fetch-only worker declares neither, so emitting the dispatch would
    /// hand the compiler a class calling codecs it does not inherit. lean core is a
    /// worker of exactly this shape, so it has to compile as written.
    /// </summary>
    [Fact]
    public void AppHostingNoActorGetsNoDispatchAndStillCompiles()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Empty(run.DefectIds);
        Assert.Empty(run.GeneratedCs);
        Assert.Equal("no errors", run.ErrorReport);
        // The ESM module is still projected: binding nothing to an actor is not binding nothing.
        Assert.Contains("export default class Site extends WorkerEntrypoint", run.GeneratedJs);
        Assert.Contains("""KV: wrapIKvNamespace(env.KV, "KV"),""", run.GeneratedJs);
        Assert.DoesNotContain("IActorRuntime", run.GeneratedJs);
    }

    /// <summary>
    /// A method that fills no handler slot cannot be dispatched on a worker, so it is refused
    /// instead of being dropped silently.
    /// </summary>
    [Fact]
    public void PublicWorkerMemberOutsideAHandlerSlotIsDiagnosed()
    {
        var run = GeneratorHarness.Run(TestSources.StrayMemberWorker);
        Assert.Equal(["CFW014"], run.DefectIds);
        Assert.Contains("Warmup", run.Defects.Single().GetMessage());
    }
}
