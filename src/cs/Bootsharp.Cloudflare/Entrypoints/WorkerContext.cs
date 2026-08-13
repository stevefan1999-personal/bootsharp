namespace Bootsharp.Cloudflare;

/// <summary>Per-request workerd <c>env</c> so singleton services can JSImport bindings.</summary>
/// <remarks>
/// Generic over the app's env interface, which means the slot is per closed type rather
/// than per app. Give the app a shorter handle with a global using alias, e.g.
/// <c>global using WorkerContext = Bootsharp.Cloudflare.WorkerContext&lt;MyApp.ICloudflareEnv&gt;;</c>.
/// </remarks>
public static class WorkerContext<TEnv> where TEnv : class
{
    private static readonly AsyncLocal<TEnv?> Current = new();

    public static TEnv Env =>
        Current.Value ?? throw new InvalidOperationException("Worker env is not bound to this call.");

    /// <summary>
    /// Binds the env for the current async flow. Public rather than internal because the entrypoint
    /// that receives the handle from workerd now lives in the app's assembly, not in this one.
    /// </summary>
    public static void Set(TEnv env) => Current.Value = env;

    public static void Clear() => Current.Value = null;
}
