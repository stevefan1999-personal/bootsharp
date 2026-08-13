namespace Bootsharp.Cloudflare;

/// <summary>
/// Cloudflare product bindings from workers-types that this sample does not
/// wrangler-bind. Add <c>[assembly: Import]</c> and a JS adapter when you use them.
/// <c>connect()</c> / streams / <c>Response</c> stay on the JS host.
/// </summary>
public interface IAnalyticsEngineDataset
{
    void WriteDataPoint(AnalyticsEngineDataPoint? point);
}

/// <summary>JS <c>AnalyticsEngineDataPoint</c> (indexes/blobs as strings).</summary>
public sealed record AnalyticsEngineDataPoint
{
    public string[]? Indexes { get; init; }
    public double[]? Doubles { get; init; }
    public string[]? Blobs { get; init; }
}

/// <summary>JS <c>RateLimit</c>.</summary>
public interface IRateLimit
{
    Task<RateLimitOutcome> Limit(RateLimitOptions options);
}

/// <summary>JS <c>RateLimitOptions</c>.</summary>
public sealed record RateLimitOptions(string Key);

/// <summary>JS <c>RateLimitOutcome</c>.</summary>
public sealed record RateLimitOutcome(bool Success);

/// <summary>JS <c>Hyperdrive</c> connection fields. <c>connect()</c> (Socket) omitted.</summary>
public interface IHyperdrive
{
    string ConnectionString { get; }
    string Host { get; }
    string Ip { get; }
    int Port { get; }
    string User { get; }
    string Password { get; }
    string Database { get; }
}

/// <summary>JS <c>SecretsStoreSecret</c>.</summary>
public interface ISecretsStoreSecret
{
    Task<string> Get();
}

/// <summary>JS <c>Vectorize</c> (RC). Vector payloads are JSON (nested maps).</summary>
public interface IVectorize
{
    Task<string> Describe();
    Task<string> Query(double[] vector, VectorizeQueryOptions? options);
    Task<string> QueryById(string vectorId, VectorizeQueryOptions? options);
    Task<string> Insert(string vectorsJson);
    Task<string> Upsert(string vectorsJson);
    Task<string> DeleteByIds(string[] ids);
    Task<string> GetByIds(string[] ids);
}

/// <summary>JS <c>VectorizeQueryOptions</c> (<c>filter</c> omitted — nested unions).</summary>
public sealed record VectorizeQueryOptions
{
    public int? TopK { get; init; }
    public string? Namespace { get; init; }
    public bool? ReturnValues { get; init; }
}

/// <summary>JS <c>Ai.run</c> flattened: model name + JSON inputs → JSON outputs.</summary>
public interface IAi
{
    Task<string> Run(string model, string inputsJson);
    Task<string> Models(string? paramsJson);
}

/// <summary>JS <c>DispatchNamespace.get</c> — service stub without <c>Request</c>/<c>Response</c>.</summary>
public interface IDispatchNamespace
{
    IServiceStub Get(string name, string? argsJson);
}

/// <summary>JS <c>Fetcher.fetch</c> with string snapshots (no streams).</summary>
public interface IServiceStub
{
    Task<string> Fetch(string method, string url, string headersJson, string body);
}

/// <summary>JS <c>SendEmail.send</c> (MIME builder / raw stream omitted).</summary>
public interface ISendEmail
{
    Task<EmailSendResult> Send(EmailEnvelope message);
}

/// <summary>JS <c>EmailMessage</c> envelope.</summary>
public sealed record EmailEnvelope(string From, string To);

/// <summary>JS <c>EmailSendResult</c>.</summary>
public sealed record EmailSendResult(string MessageId);

/// <summary>JS <c>WebSearch.search</c> — options/response as JSON.</summary>
public interface IWebSearch
{
    Task<string> Search(string optionsJson);
}

/// <summary>JS <c>Flagship.get</c> flattened to string values.</summary>
public interface IFlagship
{
    Task<string> Get(string flagKey, string? defaultValue, string? contextJson);
    Task<bool> GetBooleanValue(string flagKey, bool defaultValue, string? contextJson);
    Task<string> GetStringValue(string flagKey, string defaultValue, string? contextJson);
    Task<double> GetNumberValue(string flagKey, double defaultValue, string? contextJson);
}
