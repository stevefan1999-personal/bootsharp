namespace Cloudflare.Workers;

/// <summary>
/// JS <c>R2Bucket</c>. Values are strings (no ReadableStream / ArrayBuffer / Blob).
/// Multipart upload APIs omitted (part bodies are streams).
/// </summary>
public interface IR2Bucket
{
    Task<R2ObjectInfo?> Head(string key);
    Task<IR2ObjectBody?> Get(string key);
    Task<R2ObjectInfo> Put(string key, string value, R2PutOptions? options);
    /// <summary>JS <c>delete(string)</c> (Bootsharp emits <c>$delete</c>).</summary>
    Task Delete(string key);
    /// <summary>JS <c>delete(string[])</c>.</summary>
    Task DeleteMany(string[] keys);
    Task<R2Objects> List(R2ListOptions? options);
}

/// <summary>JS <c>R2ObjectBody</c> — body helpers; metadata is copied onto the wrap.</summary>
public interface IR2ObjectBody
{
    string Key { get; }
    string Version { get; }
    long Size { get; }
    string Etag { get; }
    string HttpEtag { get; }
    string StorageClass { get; }
    Task<string> Text();
    /// <summary>JSON text of <c>json()</c>.</summary>
    Task<string> Json();
}

/// <summary>JS <c>R2Object</c> fields used by <c>head</c> / <c>list</c> / <c>put</c> (no body stream).</summary>
public sealed record R2ObjectInfo(
    string Key,
    string Version,
    long Size,
    string Etag,
    string HttpEtag,
    string StorageClass);

/// <summary>JS <c>R2ListOptions</c>.</summary>
public sealed record R2ListOptions
{
    public int? Limit { get; init; }
    public string? Prefix { get; init; }
    public string? Cursor { get; init; }
    public string? Delimiter { get; init; }
    public string? StartAfter { get; init; }
}

/// <summary>JS <c>R2PutOptions</c> (string metadata only; checksums/SSE omitted).</summary>
public sealed record R2PutOptions
{
    public string? ContentType { get; init; }
    public string? CacheControl { get; init; }
    public string? StorageClass { get; init; }
}

/// <summary>JS <c>R2Objects</c> with the truncated/cursor union flattened.</summary>
public sealed record R2Objects(
    R2ObjectInfo[] Objects,
    string[] DelimitedPrefixes,
    bool Truncated,
    string? Cursor);
