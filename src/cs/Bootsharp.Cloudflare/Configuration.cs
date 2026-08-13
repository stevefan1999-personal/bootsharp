namespace Bootsharp.Cloudflare;

/// <summary>
/// Marks the interface that projects the worker's wrangler bindings, which the entrypoint bases are
/// parameterised over and the emitted module adapts property by property.
/// </summary>
/// <remarks>
/// The binding set is app configuration, so the interface stays app-authored and the
/// library has to be told which one it is. An attribute rather than a fixed name or namespace is
/// what makes that true in both directions: an app may call it anything and put it anywhere, and a
/// type that merely happens to be called <c>ICloudflareEnv</c> is not mistaken for it.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class WorkerEnvAttribute : Attribute;

/// <summary>
/// Declares the static-asset routes the worker answers from its assets binding without booting.NET,
/// and the name of that binding.
/// </summary>
/// <remarks>
/// One declaration feeds every consumer: the emitted fetch handler tests the path against it before
/// the guest is loaded, and it is the only place the prefixes are written — the app must not keep a
/// second copy of the predicate, because two copies drift and the JS one wins.
/// Declaring it is also what tells the emitter this worker HAS an assets binding, so a worker that
/// serves assets but wants no pre-boot fast path still declares it, with no prefixes. Absent
/// entirely, the emitted module never names the binding: every request boots the guest, and a
/// request the guest declines is a 404 rather than a lookup on a binding wrangler never configured.
/// </remarks>
/// <param name="pathPrefixes">Request paths served from the binding, matched as prefixes. A prefix
/// that is too broad only misroutes to the binding, which answers 404 for what it does not hold; a
/// prefix that is missing only costs a boot, since the handler falls back to the binding whenever
/// the guest declines the request.</param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class WorkerAssetsAttribute (params string[] pathPrefixes) : Attribute
{
    /// <summary>
    /// wrangler's own default for <c>assets.binding</c>. The emitters restate it as
    /// <c>Rules.DefaultAssetsBinding</c>, because an attribute's property default is applied by its
    /// constructor and neither of them ever runs one — change the two together.
    /// </summary>
    public const string DefaultBinding = "ASSETS";

    public string[] PathPrefixes { get; } = pathPrefixes;

    /// <summary>Set this when wrangler's <c>assets.binding</c> is not the default.</summary>
    public string Binding { get; set; } = DefaultBinding;
}
