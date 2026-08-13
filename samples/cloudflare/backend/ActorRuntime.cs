namespace Cloudflare.Backend;

/// <summary>
/// The app's half of the actor dispatch surface: it binds the generated switches (the other half of
/// this partial, emitted next to <see cref="ICloudflareEnv"/>) to the registry and JSON codecs in
/// <see cref="ActorRuntimeBase{TEnv}"/>, and to the interface Bootsharp exports to the generated JS.
/// </summary>
/// <remarks>
/// Two declarations rather than one because a partial class cannot span assemblies: the codecs are
/// packaged, the switches are generated per app, so the seam between them is inheritance.
/// </remarks>
public sealed partial class ActorRuntime : ActorRuntimeBase<ICloudflareEnv>, IActorRuntime;
