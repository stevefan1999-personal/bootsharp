namespace Bootsharp.Cloudflare;

/// <summary>
/// JS <c>KVNamespace</c>. Text get/put only — <c>"json"</c>/<c>"arrayBuffer"</c>/<c>"stream"</c>
/// overloads and bulk <c>get(string[])</c> are not imported (Bootsharp: one signature per name).
/// </summary>
public interface IKvNamespace
{
    Task<string?> Get(string key);
    Task Put(string key, string value, KvPutOptions? options);
    /// <summary>JS <c>delete</c> (Bootsharp emits <c>$delete</c>).</summary>
    Task Delete(string key);
    Task<KvListResult> List(KvListOptions? options);
    Task<KvGetWithMetadataResult> GetWithMetadata(string key);
}

/// <summary>JS <c>KVNamespaceListOptions</c>.</summary>
public sealed record KvListOptions
{
    public int? Limit { get; init; }
    public string? Prefix { get; init; }
    public string? Cursor { get; init; }
}

/// <summary>JS <c>KVNamespacePutOptions</c>. <c>metadata</c> is JSON text (TS <c>any</c>).</summary>
public sealed record KvPutOptions
{
    public int? Expiration { get; init; }
    public int? ExpirationTtl { get; init; }
    public string? Metadata { get; init; }
}

/// <summary>JS <c>KVNamespaceListKey</c>. Metadata is JSON text.</summary>
public sealed record KvListKey(string Name, int? Expiration, string? Metadata);

/// <summary>JS <c>KVNamespaceListResult</c> with the <c>list_complete</c> union flattened.</summary>
public sealed record KvListResult(bool ListComplete, KvListKey[] Keys, string? Cursor, string? CacheStatus);

/// <summary>JS <c>KVNamespaceGetWithMetadataResult</c>. Metadata is JSON text.</summary>
public sealed record KvGetWithMetadataResult(string? Value, string? Metadata, string? CacheStatus);
