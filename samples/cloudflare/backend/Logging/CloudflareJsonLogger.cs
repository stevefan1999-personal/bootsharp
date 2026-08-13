using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cloudflare.Backend.Logging;

/// <summary>
/// Renders every entry as one flat JSON object and hands it to <see cref="ILogSink"/>, which parses
/// it in the isolate so Workers Logs sees indexable fields rather than a rendered line.
/// </summary>
/// <remarks>
/// The object is built by hand rather than serialized (reflection-based serialization is disabled
/// for this assembly), and it is flat because that is what the log index wants — a nested object
/// would have to be queried by path.
/// </remarks>
internal sealed class CloudflareJsonLogger(string category, ILogSink sink) : ILogger
{
    /// <summary>Fields the entry owns; a state key colliding with one of them is dropped.</summary>
    private static readonly string[] reservedFields =
        ["time", "level", "category", "eventId", "message", "exception"];

    /// <summary>Key under which the logging infrastructure passes the unrendered message template.</summary>
    private const string originalFormatKey = "{OriginalFormat}";

    /// <summary>
    /// Scopes are not projected. Nothing in the worker opens one, and a scope stack would have to
    /// live in an AsyncLocal to survive the await points every handler has.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel level) => level != LogLevel.None;

    public void Log<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        sink.Write((int)level, BuildEntry(level, eventId, state, exception, formatter(state, exception)));
    }

    private string BuildEntry<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, string message)
    {
        var entry = new StringBuilder(256);
        entry.Append("{\"time\":\"").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append('"');
        entry.Append(",\"level\":\"").Append(LevelName(level)).Append('"');
        entry.Append(",\"category\":").Append(Json.Quote(category));
        if (eventId.Id != 0) entry.Append(",\"eventId\":").Append(eventId.Id);
        entry.Append(",\"message\":").Append(Json.Quote(message));
        if (exception is not null) entry.Append(",\"exception\":").Append(Json.Quote(exception.ToString()));
        AppendState(entry, state);
        return entry.Append('}').ToString();
    }

    /// <summary>
    /// Flattens the message template's arguments onto the entry. Carrying them as fields of their
    /// own is the point of structured logging here: the log index can filter on a field, but not on
    /// a value baked into the rendered message.
    /// </summary>
    private static void AppendState<TState>(StringBuilder entry, TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs) return;
        foreach (var (key, value) in pairs)
        {
            // Duplicate keys leave the parsed object ambiguous, so the entry's own fields win.
            if (key == originalFormatKey || Array.IndexOf(reservedFields, key) >= 0) continue;
            entry.Append(',').Append(Json.Quote(key)).Append(':').Append(Value(value));
        }
    }

    private static string Value(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => Json.Quote(text),
        // Numbers stay JSON numbers so the log index can range-query them; NaN and the infinities
        // have no JSON literal and fall through to their text form.
        IFormattable number when IsFiniteNumber(value) => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Json.Quote(value.ToString())
    };

    private static bool IsFiniteNumber(object value) => value switch
    {
        double number => double.IsFinite(number),
        float number => float.IsFinite(number),
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => true,
        _ => false
    };

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "information",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "none"
    };
}
