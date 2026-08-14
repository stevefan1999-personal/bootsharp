using Bootsharp;
using Llvm.Exercise;
using System.Reflection;

// No imports: the lane is about handles and marshaling, not about logging.
[assembly: Export(typeof(IWorker), typeof(IActorRuntime))]

// Wired by hand rather than through a container: two services, and a DI container would be among
// the largest things in the bundle.
Bind<IWorker>(new Worker());
Bind<IActorRuntime>(new ActorRuntime());

static void Bind<T> (T handler) =>
    Modules.Exports.Values.Single(module => module.Handler == typeof(T)).Factory(handler!);

/// <summary>How Bootsharp names what it emits; identical to what every Cloudflare worker declares.</summary>
public static class Prefs
{
    [RenameModule]
    public static string RenameModule (Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember (MemberInfo member, string @default) =>
        member.DeclaringType == typeof(IExerciseEnv) ? member.Name : @default;
}
