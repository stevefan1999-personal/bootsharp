using Microsoft.CodeAnalysis;
using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// CFW020–CFW029. Two properties per diagnostic, both load-bearing: the span it underlines, because
/// a refusal that points at the wrong place sends the reader to the wrong code; and whether the call
/// site still emits, because an error must emit nothing (there is no reflective binder to fall back
/// to) while a warning must emit anyway or its subject would be unreachable.
/// </summary>
public class MinimalApiDiagnosticLocationTests
{
    [Fact]
    public void PatternDiagnosticsPointAtThePattern ()
    {
        var notConstant = MinimalApiHarness.Run(BindingSources.App(
            """const string route = "/a"; var chosen = route + "/b"; app.MapGet(chosen, () => "ok");"""));
        Assert.Equal(["CFW021"], notConstant.DefectIds);
        Assert.Equal(["chosen"], notConstant.Underlined);

        var malformed = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a/{id", (string id) => id);"""));
        Assert.Equal(["CFW022"], malformed.DefectIds);
        Assert.Equal(["\"/a/{id\""], malformed.Underlined);

        var constrained = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a/{id:regex(^d+$)}", (string id) => id);"""));
        Assert.Equal(["CFW023"], constrained.DefectIds);
        Assert.Equal(["\"/a/{id:regex(^d+$)}\""], constrained.Underlined);
    }

    /// <summary>
    /// Parameter diagnostics point at the parameter's own name, not at the call: a handler with
    /// three parameters and one unbindable one has to say which.
    /// </summary>
    [Fact]
    public void ParameterDiagnosticsPointAtTheParameter ()
    {
        var byReference = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (IClock clock, string q, ref int id) => q);"""));
        Assert.Equal(["CFW026"], byReference.DefectIds);
        Assert.Equal(["id"], byReference.Underlined);

        var unparsable = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (string q, [FromQuery] IUnregistered thing) => q);"""));
        Assert.Equal(["CFW026"], unparsable.DefectIds);
        Assert.Equal(["thing"], unparsable.Underlined);

        var form = MinimalApiHarness.Run(BindingSources.App(
            """app.MapPost("/a", ([FromForm] string name) => name);"""));
        Assert.Equal(["CFW026"], form.DefectIds);
        Assert.Equal(["name"], form.Underlined);
    }

    /// <summary>
    /// The refusal raised latest — whether an unregistered complex type is a service is only
    /// answerable once every registration in the compilation has been seen, which is after the
    /// per-call-site pass that held the parameter symbols — still underlines the parameter.
    /// </summary>
    /// <remarks>It used to underline the whole invocation, because the parameter's location was not
    /// carried past that pass. <c>EndpointParameter.At</c> is what carries it.</remarks>
    [Fact]
    public void ServiceOrBodyRefusalPointsAtTheParameterItRefused ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (IClock clock, IUnregistered mystery) => "ok");"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Equal(["mystery"], run.Underlined);
        Assert.Contains("Parameter 'mystery'", run.DefectReport);
    }

    [Fact]
    public void RouteNameDiagnosticPointsAtTheParameterThatClaimedIt ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a/{id}", ([FromRoute(Name = "identifier")] string id) => id);"""));
        Assert.Equal(["CFW024"], run.DefectIds);
        Assert.Equal(["id"], run.Underlined);
        Assert.Contains("binds from route value 'identifier'", run.DefectReport);
    }

    [Fact]
    public void HandlerDiagnosticPointsAtTheHandlerArgument ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """Delegate stored = () => "ok"; app.MapGet("/a", stored);"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Equal(["stored"], run.Underlined);
    }

    /// <summary>
    /// The two whole-call-site refusals — an ambiguous route and an unwritable return type — point
    /// at the invocation, because neither belongs to any one argument.
    /// </summary>
    [Fact]
    public void WholeCallSiteDiagnosticsPointAtTheInvocation ()
    {
        var ambiguous = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "one");""",
            """app.MapGet("/a", () => "two");"""));
        Assert.Equal(["CFW025"], ambiguous.DefectIds);
        Assert.Equal(["""app.MapGet("/a", () => "two")"""], ambiguous.Underlined);

        var untyped = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", () => (object)1);"""));
        Assert.Equal(["CFW028"], untyped.DefectIds);
        Assert.Equal(["""app.MapGet("/a", () => (object)1)"""], untyped.Underlined);
    }
}

/// <summary>
/// Which refusals stop the emission. An error must, because the un-intercepted <c>Map*</c> body it
/// falls back to throws by design; a warning must not, or the shape it warns about would never run.
/// </summary>
public class MinimalApiDiagnosticSeverityTests
{
    [Theory]
    [InlineData("""var route = string.Concat("/a", "/b"); app.MapGet(route, () => "ok");""", "CFW021")]
    [InlineData("""app.MapGet("/a/{id", (string id) => id);""", "CFW022")]
    [InlineData("""app.MapGet("/a/{id:regex(^d+$)}", (string id) => id);""", "CFW023")]
    [InlineData("""app.MapGet("/a/{id}", ([FromRoute(Name = "other")] string id) => id);""", "CFW024")]
    [InlineData("""app.MapGet("/a", (IUnregistered mystery) => "ok");""", "CFW026")]
    [InlineData("""app.MapGet("/a", () => (object)1);""", "CFW028")]
    public void ErrorsEmitNothingAtAll (string map, string expected)
    {
        var run = MinimalApiHarness.Run(BindingSources.App(map));
        Assert.Equal([expected], run.DefectIds);
        Assert.Equal([DiagnosticSeverity.Error], run.Severities);
        Assert.Empty(run.Generated);
    }

    /// <summary>
    /// A refusal takes down its own call site, not its neighbours: the build has already failed, so
    /// the file's remaining job is to be readable next to the error rather than to be absent.
    /// </summary>
    [Fact]
    public void RefusalRemovesOnlyItsOwnCallSite ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/ok", () => "fine");""",
            """app.MapGet("/bad", () => (object)1);"""));
        Assert.Equal(["CFW028"], run.DefectIds);
        Assert.Single(run.Interceptors);
        Assert.Equal(1, run.InterceptsLocationCount);
        Assert.DoesNotContain("\"/bad\"", run.Generated);
    }

    /// <summary>
    /// Warnings are the two cases where the shape still binds and the point is only that it cannot
    /// do what the author probably meant.
    /// </summary>
    [Fact]
    public void WarningsStillEmit ()
    {
        var body = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", ([FromBody] Todo todo) => todo.Id);"""));
        Assert.Equal(["CFW027"], body.DefectIds);
        Assert.Equal([DiagnosticSeverity.Warning], body.Severities);
        Assert.NotEmpty(body.Generated);
        Assert.Equal("no errors", body.ErrorReport);

        var ambiguous = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "one");""",
            """app.MapGet("/a", () => "two");"""));
        Assert.Equal([DiagnosticSeverity.Warning], ambiguous.Severities);
        Assert.NotEmpty(ambiguous.Generated);

        var unserializable = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", () => new Clock());"""));
        Assert.Equal(["CFW029"], unserializable.DefectIds);
        Assert.Equal([DiagnosticSeverity.Warning], unserializable.Severities);
        Assert.NotEmpty(unserializable.Generated);
    }

    /// <summary>
    /// A type a generated context already covers is not warned about — and neither are the
    /// primitives and enums every generated context carries anyway.
    /// </summary>
    [Fact]
    public void SerializableAndBuiltInTypesAreNotWarnedAbout ()
    {
        foreach (var map in new[] {
                     """app.MapGet("/a", () => new Todo(1, "x"));""",
                     """app.MapGet("/a", () => 1);""",
                     """app.MapGet("/a", () => Sort.Asc);""",
                     """app.MapGet("/a", () => true);"""
                 })
            Assert.Empty(MinimalApiHarness.Run(BindingSources.App(map)).DefectIds);
    }

    /// <summary>
    /// Ambiguity is per verb and pattern. The same pattern under different verbs is an ordinary
    /// two-endpoint table, and the same verb under different patterns obviously is.
    /// </summary>
    [Fact]
    public void AmbiguityIsOnlyReportedForTheSameVerbAndPattern ()
    {
        Assert.Empty(MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "read");""",
            """app.MapPost("/a", () => "write");""")).DefectIds);
        Assert.Empty(MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "one");""",
            """app.MapGet("/b", () => "two");""")).DefectIds);
    }

    /// <summary>Every refusal names its own reason, so the message is not a generic "unsupported".</summary>
    [Fact]
    public void EveryRefusalCarriesItsReason ()
    {
        Assert.Contains("Inline the pattern or make it a const",
            MinimalApiHarness.Run(BindingSources.App("""var r = string.Concat("/a"); app.MapGet(r, () => "ok");""")).DefectReport);
        Assert.Contains("System.Text.RegularExpressions",
            MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a/{id:regex(^d$)}", (string id) => id);""")).DefectReport);
        Assert.Contains("no multipart or urlencoded reader",
            MinimalApiHarness.Run(BindingSources.App("""app.MapPost("/a", ([FromForm] string name) => name);""")).DefectReport);
        Assert.Contains("passed by reference",
            MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (ref int id) => id);""")).DefectReport);
        Assert.Contains("it has no TryParse",
            MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", ([FromQuery] IUnregistered thing) => "ok");""")).DefectReport);
    }
}
