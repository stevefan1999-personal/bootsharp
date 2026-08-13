namespace Cloudflare.Workers;

/// <summary>
/// Bootsharp instance imports do not await <c>Task&lt;int&gt;</c>, so workerd
/// <c>JsRpcProperty</c> thenables hit the int marshaler. Return this record instead.
/// </summary>
public sealed record RpcInt(int Value);

/// <summary>
/// JS <c>getAlarm(): number | null</c> as a closed shape: a null result means no alarm is
/// scheduled, otherwise <see cref="ScheduledTimeMs"/> is epoch milliseconds. Boxed because
/// the wasm marshaler rejects a promise of a nullable primitive (<c>Task&lt;double?&gt;</c>).
/// </summary>
public sealed record AlarmTime(double ScheduledTimeMs);

/// <summary>JS <c>DurableObjectId</c> (<c>toString()</c> is <see cref="Value"/>).</summary>
public sealed record DurableObjectId(string Value, string? Name = null, string? Jurisdiction = null);

/// <summary>JS <c>DurableObjectNamespaceGetDurableObjectOptions</c>.</summary>
public sealed record DurableObjectNamespaceGetOptions
{
    public string? LocationHint { get; init; }
    public string? RoutingMode { get; init; }
}

/// <summary>
/// JS <c>DurableObjectState</c>. WebSocket hibernation, facets, and
/// <c>blockConcurrencyWhile</c> are omitted (callbacks / host types).
/// </summary>
public interface IDurableObjectState
{
    string Id { get; }
    IDurableObjectStorage Storage { get; }
    void Abort(string? reason);
}

/// <summary>
/// JS <c>DurableObjectStorage</c>. <c>get</c>/<c>put</c> are string-valued
/// (TS <c>T</c> erased). <c>transaction</c> / <c>transactionSync</c> omitted.
/// </summary>
public interface IDurableObjectStorage
{
    Task<string?> Get(string key);
    Task Put(string key, string value);
    /// <summary>JS <c>delete(key)</c> → <c>$delete</c>; returns whether the key existed.</summary>
    Task<bool> Delete(string key);
    Task DeleteAll();
    /// <summary>JSON object of listed key/values (TS returns <c>Map</c>).</summary>
    Task<string> List(DurableObjectListOptions? options);
    /// <summary>Scheduled alarm, or null when none is set. (TS <c>number | null</c>.)</summary>
    Task<AlarmTime?> GetAlarm();
    Task SetAlarm(double scheduledTime);
    Task DeleteAlarm();
    Task Sync();
    Task<string> GetCurrentBookmark();
    Task<string> GetBookmarkForTime(double timestampMs);
    Task<string> OnNextSessionRestoreBookmark(string bookmark);
    ISqlStorage Sql { get; }
    ISyncKvStorage Kv { get; }
}

/// <summary>JS <c>DurableObjectListOptions</c>.</summary>
public sealed record DurableObjectListOptions
{
    public string? Start { get; init; }
    public string? StartAfter { get; init; }
    public string? End { get; init; }
    public string? Prefix { get; init; }
    public bool? Reverse { get; init; }
    public int? Limit { get; init; }
}

/// <summary>
/// JS <c>SqlStorage</c>. <c>exec</c> is synchronous in workerd; C# gets JSON rows
/// instead of <c>SqlStorageCursor</c>.
/// </summary>
public interface ISqlStorage
{
    long DatabaseSize { get; }
    /// <summary>
    /// Legacy: JSON array of row objects; bindings are strings. BLOB cells cross as
    /// <c>{"$blob":"&lt;base64&gt;"}</c> (see <see cref="SqlGrid.RowsJson"/>).
    /// </summary>
    string Exec(string query, string[] bindings);
    /// <summary>
    /// Typed binds (<c>bindingsJson</c> is a JSON array, BLOB binds tagged as
    /// <c>{"$blob":"&lt;base64&gt;"}</c>) and a positional <see cref="SqlGrid"/>.
    /// </summary>
    SqlGrid ExecJson(string query, string? bindingsJson);
}

/// <summary>JS <c>SyncKvStorage</c> (<c>state.storage.kv</c>).</summary>
public interface ISyncKvStorage
{
    string? Get(string key);
    void Put(string key, string value);
    /// <summary>JS <c>delete</c> → <c>$delete</c>.</summary>
    bool Delete(string key);
    /// <summary>JSON object of listed key/values (TS returns an iterator of pairs).</summary>
    string List(SyncKvListOptions? options);
}

/// <summary>JS <c>SyncKvListOptions</c>.</summary>
public sealed record SyncKvListOptions
{
    public string? Start { get; init; }
    public string? StartAfter { get; init; }
    public string? End { get; init; }
    public string? Prefix { get; init; }
    public bool? Reverse { get; init; }
    public int? Limit { get; init; }
}
