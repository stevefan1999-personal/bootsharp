namespace Llvm.Exercise;

/// <summary>
/// Guest-side actor registry the emitted Durable Object class dispatches through. Exported rather
/// than library-owned because its signatures name the app's env interface.
/// </summary>
public interface IActorRuntime
{
    int ConstructDurableObject (string className, IDurableObjectState ctx, IExerciseEnv env);
    Task<string> CallDurableObject (int id, string method, string argsJson);
}

/// <summary>
/// Binds the generated dispatch switches (the other half of this partial) to the registry and JSON
/// codecs in <see cref="ActorRuntimeBase{TEnv}"/>. A partial class cannot span assemblies, so the
/// seam between the packaged codecs and the per-app switches is inheritance.
/// </summary>
public sealed partial class ActorRuntime : ActorRuntimeBase<IExerciseEnv>, IActorRuntime;
