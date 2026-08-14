namespace Cloudflare.Razor;

/// <summary>
/// The surface Bootsharp exports to the emitted module.
/// </summary>
public interface IWorker
{
    Task<HttpResponseData> Fetch (IJsRequest request, IWorkerEnv env);
}
