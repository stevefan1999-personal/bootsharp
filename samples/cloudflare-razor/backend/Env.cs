namespace Cloudflare.Razor;

/// <summary>
/// The worker's wrangler bindings, expressed in C#. Property names are the binding names.
/// </summary>
[WorkerEnv]
public interface IWorkerEnv
{
    ID1Database DB { get; }
    string ENVIRONMENT { get; }
}
