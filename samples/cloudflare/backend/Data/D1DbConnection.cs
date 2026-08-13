using System.Data;
using System.Data.Common;

namespace Cloudflare.Backend.Data;

/// <summary>ADO.NET facade over D1. Commands are async-only (workerd has no thread to block).</summary>
public sealed class D1DbConnection : DbConnection
{
    private readonly ID1Database _db;
    private ID1DatabaseSession? _session;
    private ConnectionState _state = ConnectionState.Closed;

    public D1DbConnection(ID1Database db) => _db = db;

    public override string ConnectionString { get; set; } = "D1";
    public override string Database => "d1";
    public override string DataSource => "cloudflare-d1";
    public override string ServerVersion => "d1-sqlite";
    public override ConnectionState State => _state;

    internal ID1PreparedStatement Prepare(string sql) =>
        _session?.Prepare(sql) ?? _db.Prepare(sql);

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close()
    {
        _session = null;
        _state = ConnectionState.Closed;
    }

    public override void Open()
    {
        _session = _db.WithSession("first-primary");
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        Open();
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new D1DbTransaction(this);

    protected override DbCommand CreateDbCommand() => new D1DbCommand { Connection = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
}

internal sealed class D1DbTransaction : DbTransaction
{
    public D1DbTransaction(D1DbConnection connection) => DbConnection = connection;
    protected override DbConnection DbConnection { get; }
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    public override void Commit() { }
    public override void Rollback() =>
        throw new NotSupportedException("D1 transactions are db.batch(); this facade auto-commits each command.");
}

public sealed class D1DbCommand : DbCommand
{
    private readonly CloudflareDbParameterCollection _parameters = new();
    private D1DbConnection? _connection;

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = (D1DbConnection?)value;
    }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new CloudflareDbParameter();

    public override int ExecuteNonQuery() => throw D1AsyncOnly();
    public override object? ExecuteScalar() => throw D1AsyncOnly();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw D1AsyncOnly();

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var grid = await Bind().RunGrid().ConfigureAwait(false);
        return grid.Changes;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.FieldCount == 0)
            return null;
        return reader.IsDBNull(0) ? null : reader.GetValue(0);
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var grid = await Bind().Grid().ConfigureAwait(false);
        return new CloudflareDbDataReader(grid);
    }

    private ID1PreparedStatement Bind()
    {
        if (_connection is null) throw new InvalidOperationException("Connection is closed.");
        var (sql, values) = SqliteParameterRewriter.Rewrite(CommandText, _parameters);
        var stmt = _connection.Prepare(sql);
        return values.Count == 0 ? stmt : stmt.BindJson(SqlJson.EncodeBinds(values));
    }

    private static NotSupportedException D1AsyncOnly() =>
        new("D1 is async. Use ExecuteReaderAsync / ToListAsync — workerd cannot block a thread on a Promise.");
}
