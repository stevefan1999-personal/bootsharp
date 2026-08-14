using System.Text.RegularExpressions;
using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// every public member of an actor either gets dispatch that compiles, or a diagnostic.
/// These are the tests that keep "the generator emitted code the backend cannot build" from ever
/// reaching a user who adds a second Durable Object.
/// </summary>
public class ActorDispatchTests
{
    /// <summary>Runtime helpers the emitted dispatch is allowed to call on the packaged base.</summary>
    private static readonly Regex runtimeHelpers =
        new(@"(?<![\w.])(?<name>Arg[A-Za-z]*|Json[A-Za-z]*|ReadArgs|HasArg|Track|GetActor)\(", RegexOptions.Compiled);

    [Fact]
    public void ActorOutsideTheBackendNamespaceGetsCompilableDispatchThatParsesArguments()
    {
        var run = GeneratorHarness.RunActors(TestSources.CounterActor);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        // The switch lives next to the app's env interface, so the actor is named across namespaces.
        Assert.Contains("case global::Sample.Actors.Counter counterActor:", run.GeneratedCs);
        Assert.Contains("var args = ReadArgs(argsJson);", run.GeneratedCs);
        Assert.Contains("case \"total\": return JsonInt(counterActor.Total());", run.GeneratedCs);
        Assert.Contains("case \"add\": return JsonInt(await counterActor.Add(ArgInt(args, 0, \"add\", \"amount\")));", run.GeneratedCs);
        Assert.Contains("export class Counter extends DurableObject", run.GeneratedJs);
    }

    [Fact]
    public void EverySupportedShapeCompilesAndOptionalArgumentsFallBackToTheirDefault()
    {
        var run = GeneratorHarness.RunActors(TestSources.LedgerActor);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("case \"reset\":", run.GeneratedCs);
        Assert.Contains("return JsonInt(ledgerActor.Next());", run.GeneratedCs);
        Assert.Contains("ArgStringOrNull(args, 1, \"note\", \"fallback\")", run.GeneratedCs);
        Assert.Contains("HasArg(args, 1) ? ArgInt(args, 1, \"greet\", \"times\") : 1", run.GeneratedCs);
    }

    /// <summary>
    /// The mirror of the runtime the other tests compile against is only worth something while it
    /// matches the package, so the emitted calls are checked against the real file.
    /// </summary>
    [Fact]
    public void EmittedDispatchOnlyCallsRuntimeHelpersThePackagedBaseDeclares()
    {
        var run = GeneratorHarness.RunActors(TestSources.LedgerActor, TestSources.CounterActor);
        var called = runtimeHelpers.Matches(run.GeneratedCs)
            .Select(match => match.Groups["name"].Value)
            .Distinct();
        var packaged = GeneratorHarness.PackagedRuntime;
        Assert.NotEmpty(called);
        foreach (var helper in called)
            Assert.True(packaged.Contains($" {helper}("),
                $"ActorRuntimeBase no longer declares '{helper}', which the emitted dispatch calls.");
    }

    /// <summary>
    /// Tier boundaries are enforced at generation time: a record cannot cross the JSON RPC boundary,
    /// and the method is dropped from dispatch rather than emitted as code that cannot compile.
    /// </summary>
    [Fact]
    public void UnsupportedRpcReturnTypeIsDiagnosedAndLeavesTheRestOfTheActorIntact()
    {
        var run = GeneratorHarness.RunActors(TestSources.UnsupportedReturnActor);
        Assert.Equal(["CFW011"], run.DefectIds);
        Assert.Contains("Task<Sample.Actors.Snapshot>", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("case \"load\":", run.GeneratedCs);
        Assert.DoesNotContain("wrap.load", run.GeneratedJs);
        Assert.Contains("case \"count\": return JsonInt(archiveActor.Count());", run.GeneratedCs);
    }

    [Fact]
    public void UnsupportedRpcParameterTypeIsDiagnosedAndNotEmitted()
    {
        var run = GeneratorHarness.RunActors(TestSources.UnsupportedParameterActor);
        Assert.Equal(["CFW010"], run.DefectIds);
        Assert.Contains("payload", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("case \"store\":", run.GeneratedCs);
    }

    /// <summary>
    /// The workflow's step handle reaches user code wrapped, so the callback each <c>do</c> exports
    /// is released when the step ends. Handing over the raw import instead grew Bootsharp's export
    /// registry by one delegate per step for the life of the isolate — nothing collects it, because
    /// workerd runs neither the JS finalization registry nor a NativeAOT finalizer on a schedule.
    /// </summary>
    [Fact]
    public void WorkflowStepsAreHandedOverThroughTheReleasingWrapper()
    {
        var run = GeneratorHarness.RunActors(TestSources.DemoWorkflow);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains(
            "await workflow.Run(new global::Bootsharp.Cloudflare.WorkflowEvent(payloadJson), " +
            "new global::Bootsharp.Cloudflare.ReleasingWorkflowStep(step));",
            run.GeneratedCs);
    }

    /// <summary>
    /// The first-run cliff: the dispatch is one half of a partial whose other half the app declares,
    /// and an app that hosts an actor without declaring it used to get the generated half anyway.
    /// Measured before the diagnostic existed: <b>12</b> errors, every one of them a CS0103/CS0246
    /// naming a runtime helper (<c>Track</c>, <c>GetActor</c>, <c>EnvScope</c>, <c>ReadArgs</c>,
    /// <c>ArgInt</c>, <c>JsonInt</c>) inside <c>ActorRuntime.g.cs</c> — a file the app never wrote,
    /// cannot edit, and whose errors name everything except the declaration that fixes all of them.
    /// Suppressing the emission is what makes the count 0, so both halves of that claim are asserted
    /// here: one diagnostic, and nothing else. A Durable Object and a workflow both oblige the same
    /// declaration, so both are cases of the same test rather than one standing in for the other.
    /// </summary>
    [Theory]
    [InlineData(TestSources.CounterActor, "Counter")]
    [InlineData(TestSources.DemoWorkflow, "Pipeline")]
    public void ActorWithoutTheAppsHalfOfTheDispatchIsDiagnosedRatherThanEmitted (string actor, string name)
    {
        var run = GeneratorHarness.Run(actor);
        Assert.Equal(["CFW050"], run.DefectIds);
        Assert.Empty(run.GeneratedCs);
        Assert.Equal("no errors", run.ErrorReport);
        var defect = run.Defects.Single();
        // The message is the whole point of the diagnostic: it spells the declaration to add.
        Assert.Contains(name, defect.GetMessage());
        Assert.Contains(
            "public sealed partial class ActorRuntime : " +
            "Bootsharp.Cloudflare.ActorRuntimeBase<Cloudflare.Backend.ICloudflareEnv>",
            defect.GetMessage());
        Assert.Contains("namespace 'Cloudflare.Backend'", defect.GetMessage());
        Assert.NotEqual(Microsoft.CodeAnalysis.Location.None, defect.Location);
    }

    /// <summary>
    /// A half over another env is a different type from the one the switches land in, so it leaves
    /// the same hole — and it is the likelier mistake once an app has two env-shaped interfaces,
    /// which is why the diagnostic points at that declaration rather than at the actor.
    /// </summary>
    [Fact]
    public void HalfDeclaredOverAnotherEnvDoesNotSatisfyTheDispatchItCannotInherit ()
    {
        var run = GeneratorHarness.RunApp(
            TestSources.App(actorRuntime: TestSources.ForeignActorRuntime),
            TestSources.CounterActor);
        Assert.Equal(["CFW050"], run.DefectIds);
        Assert.Empty(run.GeneratedCs);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("ActorRuntimeBase<Cloudflare.Backend.ICloudflareEnv>", run.Defects.Single().GetMessage());
    }

    /// <summary>Declaring the half is the whole fix: the diagnostic goes, the dispatch arrives.</summary>
    [Fact]
    public void DeclaringTheHalfSilencesTheDiagnosticAndEmitsTheDispatch ()
    {
        var run = GeneratorHarness.RunActors(TestSources.CounterActor);
        Assert.DoesNotContain("CFW050", run.DefectIds);
        Assert.Contains("public sealed partial class ActorRuntime", run.GeneratedCs);
        Assert.Equal("no errors", run.ErrorReport);
    }

    /// <summary>
    /// A fetch-only worker declares no half because it has no actor to dispatch to, and telling it
    /// to would be telling every lean app to carry an actor registry it never calls.
    /// </summary>
    [Fact]
    public void WorkerHostingNoActorIsNotAskedForAHalfItHasNoUseFor ()
    {
        var run = GeneratorHarness.Run(TestSources.FetchOnlyWorker);
        Assert.Empty(run.DefectIds);
        Assert.Empty(run.GeneratedCs);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("export default class Site extends WorkerEntrypoint", run.GeneratedJs);
    }

    /// <summary>
    /// workerd resolves these prototype members itself, so an RPC method that would
    /// claim one is never dispatchable — the caller would silently reach the handler instead.
    /// </summary>
    [Fact]
    public void RpcMethodClaimingAReservedPrototypeMemberIsDiagnosed()
    {
        var run = GeneratorHarness.RunActors(TestSources.ReservedNameActor);
        Assert.Equal(["CFW012"], run.DefectIds);
        Assert.Contains("fetch", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("case \"fetch\":", run.GeneratedCs);
        Assert.DoesNotContain("async fetch(...args)", run.GeneratedJs);
    }
}
