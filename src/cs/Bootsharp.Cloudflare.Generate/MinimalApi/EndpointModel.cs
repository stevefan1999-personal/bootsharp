using Bootsharp.Cloudflare.Projection;

namespace Bootsharp.Cloudflare.Generate.MinimalApi;

/// <summary>
/// Where a handler parameter's value comes from — ladder, minus the sources this
/// package has no runtime for.
/// </summary>
/// <remarks>
/// Upstream's ladder has fourteen rungs. Form and <c>IFormFile</c> are absent because a worker
/// request arrives as buffered text with no multipart reader; <c>BindAsync</c> and
/// <c>AsParameters</c> are absent because both are ways of moving binding back into user types at
/// runtime, and every one of them would need reflection or a second generator pass to reproduce.
/// Each absent rung is a diagnostic, never a silent fallback.
/// </remarks>
internal enum BindingSource
{
    Route,
    Query,
    Header,
    JsonBody,
    Service,
    /// <summary>A complex type on a body-carrying method: a service when the app registered one,
    /// the JSON body otherwise. Upstream answers this with <c>IServiceProviderIsService</c> at
    /// startup; here it is answered from the registrations the generator can see.</summary>
    ServiceOrBody,
    /// <summary>A complex type on a method whose requests carry no body, so a service is the only
    /// thing it can be — and one the app never registered is a build error rather than a startup
    /// one.</summary>
    ServiceOnly,
    HttpContext,
    HttpRequest,
    HttpResponse,
    CancellationToken,
    ClaimsPrincipal
}

/// <summary>How a string is turned into the parameter's type.</summary>
internal enum ParseKind
{
    String,
    Enum,
    Uri,
    IParsable,
    TryParseWithFormat,
    TryParse
}

/// <summary>What the handler's return value is written as.</summary>
internal enum ResponseKind
{
    Void,
    Result,
    String,
    Json
}

/// <param name="Name">The handler's parameter name; also the prefix of every local emitted for it.</param>
/// <param name="Type">Fully qualified declared type, nullable annotation included.</param>
/// <param name="ValueType">Type a string is parsed into: <paramref name="Type"/> with
/// <c>Nullable&lt;T&gt;</c> and the array unwrapped.</param>
/// <param name="LookupName">Route value, query or header key.</param>
/// <param name="Optional">Whether an absent value is acceptable rather than a 400.</param>
/// <param name="DefaultExpression">C# expression used when the value is absent, or null for
/// <c>default</c>.</param>
/// <param name="At">Where the parameter is declared, so a refusal raised after the per-call-site pass
/// — the service-or-body one, which needs the whole compilation — underlines the parameter rather
/// than the whole <c>Map*</c> call, as every other CFW026 does.</param>
internal sealed record EndpointParameter(
    string Name,
    string Type,
    string ValueType,
    BindingSource Source,
    string LookupName,
    bool Optional,
    string? DefaultExpression,
    ParseKind Parse,
    bool IsArray,
    bool IsNullableValueType,
    LocationInfo? At = null);

/// <param name="Await">Whether the handler returns a <c>Task</c>/<c>ValueTask</c>.</param>
/// <param name="ValueType">Fully qualified type of the value written, empty when there is none.</param>
/// <param name="Primitive">Whether the written type is a primitive, an enum or another type
/// <c>System.Text.Json</c>'s generated contexts carry metadata for without being asked. Such a type
/// is not worth warning about when no context declares it: the metadata is usually there anyway,
/// and a warning that fires on <c>() =&gt; 42</c> would be noise.</param>
internal sealed record EndpointResponse(
    bool Await,
    ResponseKind Kind,
    string ValueType,
    bool Primitive = false);

/// <summary>
/// One <c>Map*</c> call site, resolved: everything the interceptor for it is emitted from.
/// </summary>
/// <param name="MapMethod">The intercepted method — <c>MapGet</c>, <c>MapMethods</c>, <c>Map</c>…</param>
/// <param name="Verbs">Accepted methods, empty when the call site takes them as an argument or
/// accepts every method.</param>
/// <param name="VerbsFromArgument">Whether the accepted methods are the call's own argument
/// (<c>MapMethods</c>).</param>
/// <param name="Precedence">Inbound precedence of <paramref name="Pattern"/>, computed here so the
/// matcher does not recompute it.</param>
internal sealed record EndpointModel(
    string MapMethod,
    EquatableArray<string> Verbs,
    bool VerbsFromArgument,
    string Pattern,
    EquatableArray<EndpointParameter> Parameters,
    EndpointResponse Response,
    string HandlerType,
    decimal Precedence,
    EquatableArray<string> RouteParameterNames)
{
    /// <summary>
    /// Identity of the code an interceptor would emit, pattern text excluded.
    /// </summary>
    /// <remarks>
    /// This is what call sites are grouped by, which is upstream's <c>EndpointDelegateComparer</c>
    /// applied to this model: two call sites that would emit the same body get one interceptor
    /// carrying both <c>[InterceptsLocation]</c> attributes. The pattern is deliberately not part of
    /// it — the interceptor receives the pattern as an argument and looks its precedence up in the
    /// emitted table, so two routes with the same handler shape still share one body.
    /// </remarks>
    public string SignatureKey =>
        $"{MapMethod}|{string.Join(",", Verbs.Items)}|{VerbsFromArgument}|{HandlerType}|" +
        $"{Response.Await}{Response.Kind}{Response.ValueType}|" +
        string.Join(";", Parameters.Items.Select(static p =>
            $"{p.Name}:{p.Type}:{p.Source}:{p.LookupName}:{p.Optional}:{p.DefaultExpression}:{p.Parse}:{p.IsArray}")) +
        "|" + string.Join(",", RouteParameterNames.Items);
}

/// <summary>
/// What one <c>Map*</c> call site resolved to: a model, or the defects that stopped it.
/// </summary>
/// <remarks>
/// A call site with defects emits nothing, and the un-intercepted body it falls back to throws
/// which is the point of reporting every refusal as an error: on this platform there is no
/// reflection-based binder behind the generator to quietly take over.
/// </remarks>
internal sealed record EndpointCandidate(
    EndpointModel? Model,
    EquatableArray<Defect> Defects,
    LocationInfo? Location,
    InterceptedLocation? Interception);

/// <summary>
/// A call site's interceptable location, carried as the two values the attribute takes.
/// </summary>
/// <remarks>Roslyn's <c>InterceptableLocation</c> is an opaque class; reducing it to its version
/// and data keeps the model comparable between incremental runs, which is what lets an edit
/// elsewhere in the file reuse this one's result.</remarks>
internal sealed record InterceptedLocation(int Version, string Data, string Display);
