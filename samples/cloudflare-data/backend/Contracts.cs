namespace Cloudflare.Data;

/// <summary>
/// Exported WASM surface. The generated JS <c>WorkerEntrypoint</c> JSImports the live
/// <see cref="IJsRequest"/> and <see cref="ICloudflareEnv"/> handles into this method.
/// </summary>
/// <remarks>
/// The response is <see cref="HttpResponseData"/> — owned by
/// <c>Bootsharp.Cloudflare.AspNetCore</c>, which also owns the <c>js/runtime.mjs</c>
/// <c>toResponse</c> that reads it, so the wire contract has one definition instead of two that
/// could drift. Exported rather than library-owned because its signature names
/// <see cref="ICloudflareEnv"/>, and Bootsharp exports closed interfaces, not generic ones.
/// </remarks>
public interface IWorker
{
    Task<HttpResponseData> Fetch (IJsRequest request, ICloudflareEnv env);
}
