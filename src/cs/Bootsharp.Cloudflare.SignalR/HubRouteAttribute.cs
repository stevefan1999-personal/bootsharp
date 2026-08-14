namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// Serves this hub-hosting Durable Object at <paramref name="pathPrefix"/> from the generated
/// worker module: <c>{prefix}{room}/negotiate</c> and the WebSocket upgrade at
/// <c>{prefix}{room}</c> are answered in JavaScript the emitter writes, so the app writes none.
/// </summary>
/// <remarks>
/// The prefix is matched with a trailing slash. wrangler <c>class_name</c> is still the
/// generated <c>{TypeName}Hub</c> wrapper — this attribute only adds the worker-side route.
/// </remarks>
/// <param name="pathPrefix">URL prefix the client uses as the hub URL, e.g. <c>/chat</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HubRouteAttribute (string pathPrefix) : Attribute
{
    /// <summary>The hub URL prefix, as declared. The emitter normalizes slashes.</summary>
    public string PathPrefix { get; } = pathPrefix;
}
