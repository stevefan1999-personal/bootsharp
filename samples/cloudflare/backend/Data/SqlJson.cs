using System.Globalization;
using System.Text;

namespace Cloudflare.Backend.Data;

/// <summary>JSON array codec for D1/DO bind values and <see cref="SqlGrid.RowsJson"/>.</summary>
internal static class SqlJson
{
    /// <summary>
    /// Tag of the object wrapping a base64 BLOB; JSON has no binary literal and
    /// <c>JSON.stringify</c> flattens an <c>ArrayBuffer</c> to <c>{}</c>.
    /// </summary>
    public const string BlobTag = "$blob";

    public static string EncodeBinds(IReadOnlyList<object?> values)
    {
        var sb = new StringBuilder(values.Count * 8 + 2);
        sb.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendValue(sb, values[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static string EncodeBatch(IReadOnlyList<(string Sql, IReadOnlyList<object?> Values)> statements)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < statements.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var (sql, values) = statements[i];
            sb.Append("{\"sql\":");
            AppendJsonString(sb, sql);
            sb.Append(",\"params\":");
            sb.Append(EncodeBinds(values));
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static void AppendValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;
            case ulong u:
                sb.Append(u.ToString(CultureInfo.InvariantCulture));
                break;
            case float or double or decimal:
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("G17", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                AppendJsonString(sb, dt.ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                AppendJsonString(sb, dto.ToString("o", CultureInfo.InvariantCulture));
                break;
            case Guid g:
                AppendJsonString(sb, g.ToString());
                break;
            case byte[] bytes:
                sb.Append("{\"").Append(BlobTag).Append("\":");
                AppendJsonString(sb, Convert.ToBase64String(bytes));
                sb.Append('}');
                break;
            default:
                AppendJsonString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
