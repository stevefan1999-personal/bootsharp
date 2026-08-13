using System.Data;
using System.Data.Common;

namespace Cloudflare.Backend.Data;

/// <summary>ADO.NET facade over Durable Object <c>storage.sql</c> (synchronous in workerd).</summary>
public sealed class DoSqliteDbConnection : DbConnection
{
    private readonly ISqlStorage _sql;
    private ConnectionState _state = ConnectionState.Closed;

    public DoSqliteDbConnection(ISqlStorage sql) => _sql = sql;

    public override string ConnectionString { get; set; } = "DoSqlite";
    public override string Database => "do-sqlite";
    public override string DataSource => "cloudflare-do";
    public override string ServerVersion => "workerd-sqlite";
    public override ConnectionState State => _state;

    internal ISqlStorage Sql => _sql;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new DoSqliteDbTransaction(this);
    protected override DbCommand CreateDbCommand() => new DoSqliteDbCommand { Connection = this };
}

internal sealed class DoSqliteDbTransaction : DbTransaction
{
    public DoSqliteDbTransaction(DoSqliteDbConnection connection) => DbConnection = connection;
    protected override DbConnection DbConnection { get; }
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    public override void Commit() { }
    public override void Rollback() =>
        throw new NotSupportedException("DO SQL transactions need storage.transactionSync; not imported yet.");
}

public sealed class DoSqliteDbCommand : DbCommand
{
    private readonly CloudflareDbParameterCollection _parameters = new();
    private DoSqliteDbConnection? _connection;

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = (DoSqliteDbConnection?)value;
    }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new CloudflareDbParameter();

    public override int ExecuteNonQuery()
    {
        var grid = Exec();
        return grid.Changes;
    }

    public override object? ExecuteScalar()
    {
        using var reader = ExecuteDbDataReader(CommandBehavior.Default);
        if (!reader.Read() || reader.FieldCount == 0) return null;
        return reader.IsDBNull(0) ? null : reader.GetValue(0);
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        new CloudflareDbDataReader(Exec());

    private SqlGrid Exec()
    {
        if (_connection is null) throw new InvalidOperationException("Connection is closed.");
        var (sql, values) = SqliteParameterRewriter.Rewrite(CommandText, _parameters);
        return _connection.Sql.ExecJson(sql, values.Count == 0 ? "[]" : SqlJson.EncodeBinds(values));
    }
}
