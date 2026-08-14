namespace Interleave.Harness;

/// <summary>Response snapshot the emitted worker module turns back into a workerd Response.</summary>
public sealed record HarnessResponse(int Status, string HeadersJson, string Body);

/// <summary>
/// The generated worker entrypoint exists only so the publish task emits the module the harness
/// imports <c>wrapEnv</c> and <c>wrapState</c> from; nothing routes to it.
/// </summary>
public interface IWorker
{
    Task<HarnessResponse> Fetch (IJsRequest request, IHarnessEnv env);
}

/// <summary>
/// What the hand-written Durable Object module calls into. The shape mirrors the generated actor
/// dispatch (<c>ActorRuntimeBase</c>): construct once per actor incarnation, then dispatch by id.
/// It is hand-written here because the Durable Object generator projects RPC methods only —
/// <c>webSocketMessage</c>/<c>webSocketClose</c>/<c>alarm</c> are reserved names it refuses
/// (Projection/Rules.cs:80) and no handler slot exists for them yet.
/// </summary>
public interface IHub
{
    /// <summary>
    /// Binds one actor incarnation to its scope (the Durable Object's name) and returns the
    /// dispatch id. Scope rather than incarnation is what keys the trace: each scenario gets its
    /// own Durable Object, so scenarios cannot contaminate each other, while a hibernation wake —
    /// which rebuilds the actor under the same name — still continues the same conversation.
    /// </summary>
    int Construct (string scope, IDurableObjectState ctx, IHarnessEnv env);
    /// <summary>The hub-method-shaped handler. <paramref name="program"/> is documented on <see cref="Interleave.Harness.Hub"/>.</summary>
    Task<string> Message (int actor, string connection, string program);
    /// <summary>The DO alarm handler, running the same program grammar.</summary>
    Task<string> Alarm (int actor, string program);
    Task<string> Closed (int actor, string connection, int code);
    /// <summary>Writes a JS-observed event into the same monotonic trace, so one order exists.</summary>
    void Note (int actor, string @event, string detail);
    /// <summary>The scope's trace plus its counters, as JSON.</summary>
    string Report (int actor);
    void Reset (int actor);
}
