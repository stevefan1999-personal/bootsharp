using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cloudflare.Backend.Data;

internal sealed class CloudflareDbParameter : DbParameter
{
    public override DbType DbType { get; set; } = DbType.String;
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; } = true;
    public override string? ParameterName { get; set; } = "";
    public override int Size { get; set; }
    public override string? SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Default;
    public override object? Value { get; set; }
    public override void ResetDbType() => DbType = DbType.String;
}

internal sealed class CloudflareDbParameterCollection : DbParameterCollection
{
    private readonly List<CloudflareDbParameter> _items = [];

    public override int Count => _items.Count;
    public override object SyncRoot => ((ICollection)_items).SyncRoot;
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;
    public override bool IsSynchronized => false;

    public IReadOnlyList<object?> Values
    {
        get
        {
            var values = new object?[_items.Count];
            for (var i = 0; i < _items.Count; i++) values[i] = _items[i].Value;
            return values;
        }
    }

    public override int Add(object value)
    {
        _items.Add(Cast(value));
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values) Add(value);
    }

    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains(Cast(value));
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf(Cast(value));

    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _items.Count; i++)
            if (NamesEqual(_items[i].ParameterName, parameterName)) return i;
        return -1;
    }

    public override void Insert(int index, object value) => _items.Insert(index, Cast(value));
    public override void Remove(object value) => _items.Remove(Cast(value));
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = Cast(value);
    protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);

    private static CloudflareDbParameter Cast(object value) => value switch
    {
        CloudflareDbParameter ours => ours,
        DbParameter p => new CloudflareDbParameter
        {
            ParameterName = p.ParameterName,
            Value = p.Value,
            DbType = p.DbType,
            Direction = p.Direction,
            IsNullable = p.IsNullable,
            Size = p.Size
        },
        _ => throw new InvalidCastException("Cloudflare SQL commands only accept DbParameter values.")
    };

    private static bool NamesEqual(string? left, string? right) =>
        string.Equals(TrimMarker(left), TrimMarker(right), StringComparison.OrdinalIgnoreCase);

    internal static string TrimMarker(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return name[0] is '@' or ':' or '$' or '?' ? name[1..] : name;
    }
}

internal static class SqliteParameterRewriter
{
    private static readonly Regex Named = new(@"[@:$][A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    public static (string Sql, IReadOnlyList<object?> Values) Rewrite(string commandText, CloudflareDbParameterCollection parameters)
    {
        if (parameters.Count == 0) return (commandText, Array.Empty<object?>());
        if (!Named.IsMatch(commandText)) return (commandText, parameters.Values);

        var values = new List<object?>(parameters.Count);
        var sql = Named.Replace(commandText, match =>
        {
            var name = match.Value;
            var index = parameters.IndexOf(name);
            if (index < 0) index = parameters.IndexOf(CloudflareDbParameterCollection.TrimMarker(name));
            if (index < 0)
                throw new InvalidOperationException($"No parameter named '{name}'.");
            values.Add(parameters[index].Value);
            return "?";
        });
        return (sql, values);
    }
}

internal sealed class CloudflareDbDataReader : DbDataReader
{
    private readonly SqlGrid _grid;
    private readonly JsonDocument _document;
    private readonly JsonElement[] _rows;
    private int _row = -1;
    private bool _closed;

    public CloudflareDbDataReader(SqlGrid grid)
    {
        _grid = grid;
        _document = JsonDocument.Parse(string.IsNullOrWhiteSpace(grid.RowsJson) ? "[]" : grid.RowsJson);
        var rows = new List<JsonElement>();
        foreach (var row in _document.RootElement.EnumerateArray()) rows.Add(row);
        _rows = [.. rows];
    }

    public override int FieldCount => _grid.Columns.Length;
    public override bool HasRows => _rows.Length > 0;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => _grid.Changes;
    public override int Depth => 0;
    public override int VisibleFieldCount => FieldCount;

    public override string GetName(int ordinal) => _grid.Columns[ordinal];

    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < _grid.Columns.Length; i++)
            if (string.Equals(_grid.Columns[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        throw new IndexOutOfRangeException(name);
    }

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties |
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetFieldType(int ordinal) => Kind(ordinal) switch
    {
        JsonValueKind.Number => typeof(long),
        JsonValueKind.True or JsonValueKind.False => typeof(bool),
        JsonValueKind.Null => typeof(DBNull),
        JsonValueKind.Object when IsBlob(Cell(ordinal), out _) => typeof(byte[]),
        _ => typeof(string)
    };

    public override object GetValue(int ordinal)
    {
        var cell = Cell(ordinal);
        return cell.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => cell.TryGetInt64(out var l) ? l : cell.GetDouble(),
            JsonValueKind.String => cell.GetString() ?? "",
            JsonValueKind.Object when IsBlob(cell, out var encoded) => Blob(encoded),
            _ => cell.GetRawText()
        };
    }

    public override int GetValues(object[] values)
    {
        var n = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < n; i++) values[i] = GetValue(i);
        return n;
    }

    public override bool IsDBNull(int ordinal) => Cell(ordinal).ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    public override bool GetBoolean(int ordinal) => Cell(ordinal).GetBoolean();
    public override byte GetByte(int ordinal) => (byte)GetInt64(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (!IsBlob(Cell(ordinal), out var encoded))
            throw new InvalidCastException($"Column '{GetName(ordinal)}' is not a BLOB.");
        var bytes = Blob(encoded);
        if (buffer is null) return bytes.Length;
        var available = Math.Max(0, bytes.Length - dataOffset);
        var room = Math.Max(0, Math.Min(length, buffer.Length - bufferOffset));
        var copied = (int)Math.Min(available, room);
        if (copied > 0) Array.Copy(bytes, dataOffset, buffer, bufferOffset, copied);
        return copied;
    }
    public override char GetChar(int ordinal) => GetString(ordinal)[0];
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => Guid.Parse(GetString(ordinal));
    public override short GetInt16(int ordinal) => (short)GetInt64(ordinal);
    public override int GetInt32(int ordinal) => (int)GetInt64(ordinal);
    public override long GetInt64(int ordinal) => Cell(ordinal).TryGetInt64(out var l) ? l : Convert.ToInt64(Cell(ordinal).GetDouble(), CultureInfo.InvariantCulture);
    public override float GetFloat(int ordinal) => (float)GetDouble(ordinal);
    public override double GetDouble(int ordinal) => Cell(ordinal).TryGetInt64(out var l) ? l : Cell(ordinal).GetDouble();
    public override string GetString(int ordinal) => Cell(ordinal).ValueKind == JsonValueKind.String ? Cell(ordinal).GetString() ?? "" : Cell(ordinal).GetRawText();
    public override decimal GetDecimal(int ordinal) => Cell(ordinal).TryGetDecimal(out var d) ? d : (decimal)GetDouble(ordinal);
    public override DateTime GetDateTime(int ordinal) => DateTime.Parse(GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_row + 1 >= _rows.Length) return false;
        _row++;
        return true;
    }

    public override bool NextResult() => false;
    public override void Close()
    {
        _closed = true;
        _document.Dispose();
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override DataTable? GetSchemaTable() => null;

    private JsonElement Cell(int ordinal)
    {
        if (_row < 0 || _row >= _rows.Length) throw new InvalidOperationException("Read() first.");
        return _rows[_row][ordinal];
    }

    private JsonValueKind Kind(int ordinal) => _row >= 0 && _row < _rows.Length ? Cell(ordinal).ValueKind : JsonValueKind.Null;

    /// <summary>Binary cells arrive tagged (see <see cref="SqlJson.BlobTag"/>) since JSON has no binary literal.</summary>
    private static bool IsBlob(JsonElement cell, out JsonElement encoded)
    {
        encoded = default;
        if (cell.ValueKind != JsonValueKind.Object) return false;
        if (!cell.TryGetProperty(SqlJson.BlobTag, out encoded)) return false;
        return encoded.ValueKind == JsonValueKind.String;
    }

    private static byte[] Blob(JsonElement encoded) => Convert.FromBase64String(encoded.GetString() ?? "");
}
