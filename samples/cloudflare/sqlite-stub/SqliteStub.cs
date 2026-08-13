using System.Collections;
using System.Data;
using System.Data.Common;

namespace Microsoft.Data.Sqlite;

/// <summary>
/// linq2db's SQLite Microsoft provider always reflects these types even when the
/// caller supplies an existing <see cref="DbConnection"/>. They are never used to
/// talk to D1; that goes through <c>D1DbConnection</c>.
/// </summary>
public sealed class SqliteConnection : DbConnection
{
    public SqliteConnection() { }
    public SqliteConnection(string connectionString) => ConnectionString = connectionString;

    public static void ClearAllPools() { }

    public override string ConnectionString { get; set; } = "";
    public override string Database => "stub";
    public override string DataSource => "stub";
    public override string ServerVersion => "stub";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw Stub();
    public override void Close() { }
    public override void Open() => throw Stub();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw Stub();
    protected override DbCommand CreateDbCommand() => new SqliteCommand();

    private static NotSupportedException Stub() =>
        new("Microsoft.Data.Sqlite is a metadata stub; SQL runs on D1DbConnection.");
}

public sealed class SqliteCommand : DbCommand
{
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new SqliteParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => throw Stub();
    public override object? ExecuteScalar() => throw Stub();
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new SqliteParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw Stub();
    private static NotSupportedException Stub() =>
        new("Microsoft.Data.Sqlite is a metadata stub; SQL runs on D1DbConnection.");
}

public sealed class SqliteParameter : DbParameter
{
    public override DbType DbType { get; set; } = DbType.String;
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; } = true;
    public override string ParameterName { get; set; } = "";
    public override int Size { get; set; }
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() => DbType = DbType.String;
}

public sealed class SqliteParameterCollection : DbParameterCollection
{
    private readonly List<SqliteParameter> _items = [];
    public override int Count => _items.Count;
    public override object SyncRoot => _items;
    public override int Add(object value) { _items.Add((SqliteParameter)value); return _items.Count - 1; }
    public override void AddRange(Array values) { foreach (var v in values) Add(v); }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((SqliteParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((SqliteParameter)value);
    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (SqliteParameter)value);
    public override void Remove(object value) => _items.Remove((SqliteParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = (SqliteParameter)value;
    protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
}

public sealed class SqliteTransaction : DbTransaction
{
    protected override DbConnection? DbConnection => null;
    public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
    public override void Commit() { }
    public override void Rollback() { }
}

public sealed class SqliteDataReader : DbDataReader
{
    public override int FieldCount => 0;
    public override bool HasRows => false;
    public override bool IsClosed => true;
    public override int RecordsAffected => 0;
    public override int Depth => 0;
    public override object this[int ordinal] => throw Stub();
    public override object this[string name] => throw Stub();
    public override bool GetBoolean(int ordinal) => throw Stub();
    public override byte GetByte(int ordinal) => throw Stub();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw Stub();
    public override char GetChar(int ordinal) => throw Stub();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw Stub();
    public override string GetDataTypeName(int ordinal) => throw Stub();
    public override DateTime GetDateTime(int ordinal) => throw Stub();
    public override decimal GetDecimal(int ordinal) => throw Stub();
    public override double GetDouble(int ordinal) => throw Stub();
    public override IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    public override Type GetFieldType(int ordinal) => throw Stub();
    public override float GetFloat(int ordinal) => throw Stub();
    public override Guid GetGuid(int ordinal) => throw Stub();
    public override short GetInt16(int ordinal) => throw Stub();
    public override int GetInt32(int ordinal) => throw Stub();
    public override long GetInt64(int ordinal) => throw Stub();
    public override string GetName(int ordinal) => throw Stub();
    public override int GetOrdinal(string name) => throw Stub();
    public override string GetString(int ordinal) => throw Stub();
    public override object GetValue(int ordinal) => throw Stub();
    public override int GetValues(object[] values) => 0;
    public override bool IsDBNull(int ordinal) => true;
    public override bool NextResult() => false;
    public override bool Read() => false;
    private static NotSupportedException Stub() =>
        new("Microsoft.Data.Sqlite is a metadata stub; SQL runs on D1DbConnection.");
}
