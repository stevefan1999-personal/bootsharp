namespace Cloudflare.Workers;

/// <summary>
/// Typed SQL result crossing Bootsharp. <see cref="RowsJson"/> is a JSON array of
/// arrays so numbers/nulls/bools stay JSON-typed (not stringified). Binary cells
/// (<c>SqlStorageValue</c> allows <c>ArrayBuffer</c>, which <c>JSON.stringify</c>
/// flattens to <c>{}</c>) are tagged objects: <c>{"$blob":"&lt;base64&gt;"}</c>.
/// </summary>
public sealed record SqlGrid(
    string[] Columns,
    string RowsJson,
    long LastRowId,
    int Changes,
    int RowsRead,
    int RowsWritten);
