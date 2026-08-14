using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;

namespace Bootsharp.Cloudflare.Generate.SignalR;

/// <summary>One <c>Hub</c> subclass as the dispatch emitter sees it.</summary>
/// <param name="HasDefaultConstructor">Whether generated dispatch can construct it at all.</param>
internal sealed record HubModel (
    string Name,
    string Namespace,
    bool HasDefaultConstructor,
    EquatableArray<HubMethodModel> Methods,
    EquatableArray<HubDefect> Defects,
    LocationInfo? Location);

/// <param name="WireName">Name clients invoke, honouring <c>[HubMethodName]</c>.</param>
/// <param name="Await">Whether the invocation is Task/ValueTask-returning.</param>
/// <param name="ReturnsValue">Whether a completion carries a result.</param>
/// <param name="PayloadName">Return payload type as a JSON context declares it, or null.</param>
internal sealed record HubMethodModel (
    string CsName,
    string WireName,
    bool Await,
    bool ReturnsValue,
    string? PayloadName,
    EquatableArray<HubParameter> Parameters);

/// <param name="TypeName">Fully qualified, for the cast and the <c>typeof</c>.</param>
/// <param name="PayloadName">Same type as a JSON context declares it, for the coverage check.</param>
internal sealed record HubParameter (string Name, string TypeName, string PayloadName);

/// <summary>A hub shape the emitter refuses, or accepts with a warning.</summary>
internal sealed record HubDefect (
    string Id,
    string Title,
    string Message,
    LocationInfo? Location,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error);
