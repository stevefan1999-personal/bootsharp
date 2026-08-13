using Xunit;

namespace Cloudflare.Workers.Generator.Tests;

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
        Assert.Contains("function wrapScheduledController(controller)", run.GeneratedJs);
    }

    /// <summary>
    /// an env property is adapted by its declared interface type. The KV handle uses the
    /// template adapter; the actor namespace uses the one projected from the Durable Object class.
    /// </summary>
    [Fact]
    public void EnvAdaptersAreChosenByPropertyTypeRatherThanBindingName()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker, TestSources.CounterActor);
        Assert.Contains("KV: wrapIKvNamespace(env.KV),", run.GeneratedJs);
        Assert.Contains("COUNTER: wrapICounterNamespace(env.COUNTER),", run.GeneratedJs);
        Assert.Contains("function wrapICounterNamespace(ns)", run.GeneratedJs);
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
