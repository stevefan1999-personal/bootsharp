using System.Diagnostics.CodeAnalysis;
using Bootsharp.Cloudflare.AspNetCore.Html;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bootsharp.Cloudflare.Components;

/// <summary>
/// Renders a component into the writer tier's sink.
/// </summary>
/// <remarks>
/// The seam between the two tiers of: a component renders into the same
/// <see cref="HtmlWriter"/> a compiled template writes into, so a page can be a template with a
/// component inside it, or a component on its own. It is also what keeps the opt-in tier honest
/// about streaming — <c>HtmlRootComponent.WriteHtmlTo</c> is synchronous and quiescence completes
/// before the first byte, so this yields chunked emission of a finished tree, never progressive
/// rendering. Progressive SSR lives in <c>Components.Endpoints</c>, which is not a package at all.
/// </remarks>
public static class ComponentRenderer
{
    /// <summary>Renders a component to a fragment.</summary>
    /// <param name="services">Resolves the component's <c>[Inject]</c> dependencies and, optionally,
    /// an <see cref="ILoggerFactory"/>.</param>
    /// <param name="parameters">The component's parameters, by name.</param>
    public static async Task<HtmlString> RenderAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> (
        IServiceProvider services, IDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        await using var renderer = new WorkerHtmlRenderer(services, services.GetService<ILoggerFactory>());
        var view = parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters);
        // Dispatched even though the dispatcher is inline: WriteComponentHtml asserts access, and
        // going through the dispatcher is what keeps that assertion meaningful if it ever is not.
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = renderer.BeginRenderingComponent(typeof(TComponent), view);
            // Nothing writes until every OnInitializedAsync has completed: static rendering has no
            // second pass to correct a half-rendered tree with.
            await root.QuiescenceTask;
            return HtmlString.Raw(root.ToHtmlString());
        });
    }
}

/// <summary>A <c>text/html</c> response rendered by a Razor component.</summary>
public sealed class ComponentHttpResult<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> (
    IDictionary<string, object?>? parameters, int? statusCode) : IResult
    where TComponent : IComponent
{
    public int? StatusCode { get; } = statusCode;

    public async Task ExecuteAsync (HttpContext httpContext)
    {
        if (StatusCode.HasValue) httpContext.Response.StatusCode = StatusCode.Value;
        httpContext.Response.ContentType = "text/html; charset=utf-8";
        var html = await ComponentRenderer.RenderAsync<TComponent>(httpContext.RequestServices, parameters);
        await httpContext.Response.WriteAsync(html.Value);
    }
}

/// <summary>
/// Adds <c>Component&lt;T&gt;</c> to the result factories of
/// <c>Bootsharp.Cloudflare.AspNetCore</c>.
/// </summary>
/// <remarks>
/// An extension rather than a member, because the two tiers are two packages: the writer tier owns
/// <see cref="Results"/> and must not know that Razor Components exists, or every app would pay for
/// them. Referencing this package is what makes the factory appear, which is the priced opt-in
/// describes, expressed in the API surface.
/// </remarks>
public static class ComponentResults
{
    extension (Results)
    {
        /// <summary>A <c>text/html</c> response rendered by a Razor component.</summary>
        public static IResult Component<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> (
            IDictionary<string, object?>? parameters = null, int? statusCode = null)
            where TComponent : IComponent => new ComponentHttpResult<TComponent>(parameters, statusCode);
    }

}

/// <inheritdoc cref="ComponentResults"/>
/// <remarks>Its own class because the two extension blocks lower into their containing type, and
/// one type cannot declare the same method twice.</remarks>
public static class ComponentTypedResults
{
    extension (TypedResults)
    {
        /// <inheritdoc cref="ComponentResults.Component{TComponent}(IDictionary{string, object?}, int?)"/>
        public static ComponentHttpResult<TComponent> Component<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent> (
            IDictionary<string, object?>? parameters = null, int? statusCode = null)
            where TComponent : IComponent => new(parameters, statusCode);
    }
}
