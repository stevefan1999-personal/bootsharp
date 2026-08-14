// Copied verbatim (namespace aside) from samples/cloudflare/backend/Data/SqliteCustomAdapter.cs.
// lists Data/** as sample-only forever, so each sample carries its own copy rather
// than a shared project: the ADO provider is demonstration material, not a shipped layer.

using System.Globalization;
using FreeSql.Custom;

namespace Cloudflare.Data.Ado;

/// <summary>
/// FreeSql.Provider.Custom defaults to SQL Server (TOP, SCOPE_IDENTITY, [brackets]).
/// D1/DO SQL is SQLite — LIMIT, last_insert_rowid(), and SQLite quoting.
/// </summary>
internal sealed class SqliteCustomAdapter : CustomAdapter
{
    public override SelecTopStyle SelectTopStyle => SelecTopStyle.Limit;
    public override string InsertAfterGetIdentitySql => "SELECT last_insert_rowid()";
    public override char QuoteSqlNameLeft => '"';
    public override char QuoteSqlNameRight => '"';
    public override string IsNullSql(string sql, object value) => $"ifnull({sql}, {value})";
    public override string ConcatSql(string[] objs, Type[] types) => string.Join(" || ", objs);
    public override string LambdaString_Length(string operand) => $"length({operand})";
    public override string LambdaDateTime_Now => "datetime('now')";
    public override string LambdaDateTime_UtcNow => "datetime('now')";
    public override string LambdaGuid_NewGuid => "lower(hex(randomblob(16)))";
    public override string UnicodeStringRawSql(object value, FreeSql.Internal.Model.ColumnInfo mapColumn)
    {
        if (value == null) return "NULL";
        return "'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''") + "'";
    }
}
