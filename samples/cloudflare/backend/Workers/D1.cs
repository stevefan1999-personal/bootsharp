namespace Cloudflare.Workers;

/// <summary>
/// JS <c>D1Database</c>. <c>batch(D1PreparedStatement[])</c> is omitted (array of handles).
/// Alpha <c>dump()</c> omitted (ArrayBuffer).
/// </summary>
public interface ID1Database
{
    ID1PreparedStatement Prepare(string query);
    Task<D1ExecResult> Exec(string query);
    /// <summary>JS <c>withSession</c> — bookmark or <c>first-primary</c> / <c>first-unconstrained</c>.</summary>
    ID1DatabaseSession WithSession(string? constraintOrBookmark);
    /// <summary>
    /// JSON array of <c>{ sql, params }</c> executed via <c>db.batch</c> (one D1 transaction).
    /// Returns a JSON array of <see cref="SqlGrid"/> objects.
    /// </summary>
    Task<string> BatchJson(string statementsJson);
}

/// <summary>JS <c>D1DatabaseSession</c>. <c>batch</c> omitted (same reason as <see cref="ID1Database"/>).</summary>
public interface ID1DatabaseSession
{
    ID1PreparedStatement Prepare(string query);
    string? GetBookmark();
}

/// <summary>
/// JS <c>D1PreparedStatement</c>. <c>bind(...unknown[])</c> is <see cref="Bind"/> with string
/// args (NativeAOT has no <c>params object[]</c> across the FFI). Row payloads are JSON
/// because TS <c>T</c> is caller-defined.
/// </summary>
public interface ID1PreparedStatement
{
    ID1PreparedStatement Bind(string[] values);
    /// <summary>JSON array of typed bind values (<c>[1, "x", null, true]</c>).</summary>
    ID1PreparedStatement BindJson(string valuesJson);
    /// <summary>JSON <c>D1Result</c> (<c>success</c>/<c>meta</c>/<c>results</c>).</summary>
    Task<string> All();
    /// <summary>JSON row or null (<c>first()</c> with no column).</summary>
    Task<string?> First();
    /// <summary>JS <c>first(colName)</c> — scalar JSON or null.</summary>
    Task<string?> FirstColumn(string colName);
    /// <summary>JSON <c>D1Result</c> with empty results.</summary>
    Task<string> Run();
    /// <summary>JSON array of positional rows (<c>raw()</c>).</summary>
    Task<string> Raw();
    /// <summary><c>raw({ columnNames: true })</c> as a typed grid.</summary>
    Task<SqlGrid> Grid();
    /// <summary><c>run()</c> meta as a grid (DML: <see cref="SqlGrid.LastRowId"/> / <see cref="SqlGrid.Changes"/>).</summary>
    Task<SqlGrid> RunGrid();
}

/// <summary>JS <c>D1ExecResult</c>.</summary>
public sealed record D1ExecResult(int Count, double Duration);

/// <summary>JS <c>D1Meta</c> (present inside <c>all</c>/<c>run</c> JSON).</summary>
public sealed record D1Meta(
    double Duration,
    int SizeAfter,
    int RowsRead,
    int RowsWritten,
    int LastRowId,
    bool ChangedDb,
    int Changes);
