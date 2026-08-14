using Microsoft.AspNetCore.Http.Metadata;

namespace Microsoft.AspNetCore.Mvc;

/// <summary>
/// The binding-source attributes a Minimal API handler can carry.
/// </summary>
/// <remarks>
/// <para>
/// These live in <c>Microsoft.AspNetCore.Mvc</c> upstream — in <c>Mvc.Core</c>, a shared-framework
/// assembly, which is why identity rule allows declaring them into that namespace here:
/// <c>using Microsoft.AspNetCore.Mvc;</c> above a handler is what every Minimal API sample writes,
/// and a differently-named attribute would break that for no gain.
/// </para>
/// <para>
/// Reimplemented rather than vendored, and the difference is the interfaces. Upstream's versions
/// also implement <c>IBindingSourceMetadata</c>, <c>IModelNameProvider</c> and
/// <c>IConfigureEmptyBodyBehavior</c> from <c>Microsoft.AspNetCore.Mvc.ModelBinding</c> — the MVC
/// model binder's own contracts, for a model binder that does not exist here. What the generator
/// reads is the <c>IFrom*Metadata</c> interfaces from <c>Http.Abstractions</c>, which are vendored,
/// and those are what these implement. A handler ports unchanged; an MVC controller would not, and
/// there are no controllers on this platform.
/// </para>
/// <para>
/// Binding happens at compile time, so these attributes are read by the generator,
/// never at runtime. They exist mainly as the escape hatch requires: where the
/// generator cannot tell a service parameter from a body parameter, <c>[FromServices]</c> or
/// <c>[FromBody]</c> settles it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromRouteAttribute : Attribute, IFromRouteMetadata
{
    /// <summary>Route parameter to bind from, when it differs from the parameter's name.</summary>
    public string? Name { get; set; }
}

/// <inheritdoc cref="FromRouteAttribute"/>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromQueryAttribute : Attribute, IFromQueryMetadata
{
    /// <summary>Query key to bind from, when it differs from the parameter's name.</summary>
    public string? Name { get; set; }
}

/// <inheritdoc cref="FromRouteAttribute"/>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromHeaderAttribute : Attribute, IFromHeaderMetadata
{
    /// <summary>Header to bind from, when it differs from the parameter's name.</summary>
    public string? Name { get; set; }
}

/// <inheritdoc cref="FromRouteAttribute"/>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromBodyAttribute : Attribute, IFromBodyMetadata
{
    /// <summary>Whether an empty request body binds as the default value instead of failing.</summary>
    /// <remarks>Upstream expresses this as an <c>EmptyBodyBehavior</c> enum whose <c>Default</c>
    /// member means "whatever MvcOptions says"; with no MvcOptions to consult, the tri-state
    /// collapses to the boolean it always resolved to.</remarks>
    public bool AllowEmpty { get; set; }
}

/// <inheritdoc cref="FromRouteAttribute"/>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromServicesAttribute : Attribute, IFromServiceMetadata;

/// <summary>
/// Binds a parameter from the request form.
/// </summary>
/// <remarks>Declared so that a handler using it fails with the generator's diagnostic naming the
/// <c>.Forms</c> layer, rather than with "no such attribute" — the parameter would otherwise be
/// silently bound from somewhere else.</remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromFormAttribute : Attribute, IFromFormMetadata
{
    /// <summary>Form field to bind from, when it differs from the parameter's name.</summary>
    public string? Name { get; set; }
}
