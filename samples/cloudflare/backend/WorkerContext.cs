namespace Cloudflare.Backend;

/// <summary>Per-request workerd <c>env</c> so singleton services can JSImport bindings.</summary>
public static class WorkerContext
{
    private static readonly AsyncLocal<ICloudflareEnv?> Current = new();

    public static ICloudflareEnv Env =>
        Current.Value ?? throw new InvalidOperationException("Worker env is not bound to this call.");

    internal static void Set(ICloudflareEnv env) => Current.Value = env;

    internal static void Clear() => Current.Value = null;
}
