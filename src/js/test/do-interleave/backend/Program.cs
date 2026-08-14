using Bootsharp;
using Interleave.Harness;
using System.Reflection;

[assembly: Export(typeof(IWorker), typeof(IHub))]

Bind<IWorker>(new Worker());
Bind<IHub>(new Hub());

static void Bind<T> (T handler) =>
    Modules.Exports.Values.Single(module => module.Handler == typeof(T)).Factory(handler!);

/// <summary>How Bootsharp names what it emits; identical to what every Cloudflare worker declares.</summary>
public static class Prefs
{
    [RenameModule]
    public static string RenameModule (Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember (MemberInfo member, string @default) =>
        member.DeclaringType == typeof(IHarnessEnv) ? member.Name : @default;
}
