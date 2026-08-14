namespace Cloudflare.Razor;

/// <summary>
/// The surface Bootsharp exports to the emitted module.
/// </summary>
public interface IWorker
{
    Task<HttpResponseData> Fetch (IJsRequest request, IWorkerEnv env);
}

public sealed record Health (bool Ok, string Runtime, string Environment, string Page);
