using System.Text.RegularExpressions;
using Xunit;

namespace Cloudflare.Workers.Generator.Tests;

/// <summary>
/// every public member of an actor either gets dispatch that compiles, or a diagnostic.
/// These are the tests that keep "the generator emitted code the backend cannot build" from ever
/// reaching a user who adds a second Durable Object.
/// </summary>
public class ActorDispatchTests
{
    /// <summary>Runtime helpers the emitted dispatch is allowed to call on the hand-written half.</summary>
    private static readonly Regex runtimeHelpers =
        new(@"(?<![\w.])(?<name>Arg[A-Za-z]*|Json[A-Za-z]*|ReadArgs|HasArg|Track|GetActor)\(", RegexOptions.Compiled);

    [Fact]
    public void ActorOutsideTheBackendNamespaceGetsCompilableDispatchThatParsesArguments()
    {
        var run = GeneratorHarness.Run(TestSources.CounterActor);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        // The switch lives in Cloudflare.Backend, so the actor must be named across namespaces.
        Assert.Contains("case global::Sample.Actors.Counter counterActor:", run.GeneratedCs);
        Assert.Contains("var args = ReadArgs(argsJson);", run.GeneratedCs);
        Assert.Contains("case \"total\": return JsonInt(counterActor.Total());", run.GeneratedCs);
        Assert.Contains("case \"add\": return JsonInt(await counterActor.Add(ArgInt(args, 0, \"add\", \"amount\")));", run.GeneratedCs);
        Assert.Contains("export class Counter extends DurableObject", run.GeneratedJs);
    }

    [Fact]
    public void EverySupportedShapeCompilesAndOptionalArgumentsFallBackToTheirDefault()
    {
        var run = GeneratorHarness.Run(TestSources.LedgerActor);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("case \"reset\":", run.GeneratedCs);
        Assert.Contains("return JsonRpcInt(ledgerActor.Next());", run.GeneratedCs);
        Assert.Contains("ArgStringOrNull(args, 1, \"note\", \"fallback\")", run.GeneratedCs);
        Assert.Contains("HasArg(args, 1) ? ArgInt(args, 1, \"greet\", \"times\") : 1", run.GeneratedCs);
    }

    /// <summary>
    /// The mirror of the runtime the other tests compile against is only worth something while it
    /// matches the backend, so the emitted calls are checked against the real file.
    /// </summary>
    [Fact]
    public void EmittedDispatchOnlyCallsRuntimeHelpersTheBackendDeclares()
    {
        var run = GeneratorHarness.Run(TestSources.LedgerActor, TestSources.CounterActor);
        var called = runtimeHelpers.Matches(run.GeneratedCs)
            .Select(match => match.Groups["name"].Value)
            .Distinct();
        var backend = GeneratorHarness.BackendRuntime;
        Assert.NotEmpty(called);
        foreach (var helper in called)
            Assert.True(backend.Contains($" {helper}("),
                $"backend/ActorRuntime.cs no longer declares '{helper}', which the emitted dispatch calls.");
    }

    /// <summary>
    /// Tier boundaries are enforced at generation time: a record cannot cross the JSON RPC boundary,
    /// and the method is dropped from dispatch rather than emitted as code that cannot compile.
    /// </summary>
    [Fact]
    public void UnsupportedRpcReturnTypeIsDiagnosedAndLeavesTheRestOfTheActorIntact()
    {
        var run = GeneratorHarness.Run(TestSources.UnsupportedReturnActor);
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
        var run = GeneratorHarness.Run(TestSources.UnsupportedParameterActor);
        Assert.Equal(["CFW010"], run.DefectIds);
        Assert.Contains("payload", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("case \"store\":", run.GeneratedCs);
    }

    /// <summary>
    /// workerd resolves these prototype members itself, so an RPC method that would
    /// claim one is never dispatchable — the caller would silently reach the handler instead.
    /// </summary>
    [Fact]
    public void RpcMethodClaimingAReservedPrototypeMemberIsDiagnosed()
    {
        var run = GeneratorHarness.Run(TestSources.ReservedNameActor);
        Assert.Equal(["CFW012"], run.DefectIds);
        Assert.Contains("fetch", run.Defects.Single().GetMessage());
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("case \"fetch\":", run.GeneratedCs);
        Assert.DoesNotContain("async fetch(...args)", run.GeneratedJs);
    }
}
