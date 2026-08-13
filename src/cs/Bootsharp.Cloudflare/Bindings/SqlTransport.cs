namespace Bootsharp.Cloudflare;

/// <summary>Wire constants of the SQL transport, shared by both ends of the boundary.</summary>
public static class SqlTransport
{
    /// <summary>
    /// Key of the object wrapping a base64 BLOB. JSON has no binary literal and
    /// <c>JSON.stringify</c> flattens an <c>ArrayBuffer</c> to <c>{}</c>, so binary cells and
    /// binds cross as <c>{"$blob":"&lt;base64&gt;"}</c>. The packaged <c>js/runtime.mjs</c> reads
    /// and writes the same key; an app encoding its own binds must use this constant rather than
    /// restate the literal.
    /// </summary>
    public const string BlobTag = "$blob";
}

/// <summary>
/// Typed SQL result crossing Bootsharp. <see cref="RowsJson"/> is a JSON array of
/// arrays so numbers/nulls/bools stay JSON-typed (not stringified). Binary cells
/// (<c>SqlStorageValue</c> allows <c>ArrayBuffer</c>) are tagged with
/// <see cref="SqlTransport.BlobTag"/>.
/// </summary>
public sealed record SqlGrid(
    string[] Columns,
    string RowsJson,
    long LastRowId,
    int Changes,
    int RowsRead,
    int RowsWritten);
