using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Bootsharp.Cloudflare.Logging;

/// <summary>
/// Renders every entry as one flat JSON object and hands it to <see cref="ILogSink"/>, which parses
/// it in the isolate so Workers Logs sees indexable fields rather than a rendered line.
/// </summary>
/// <remarks>
/// The object is serialized through <see cref="CloudflareJsonContext"/>: reflection-based
/// serialization is disabled for this assembly. It is flat because that is what the log index
/// wants — a nested object would have to be queried by path.
/// </remarks>
internal sealed class CloudflareJsonLogger (string category, ILogSink sink) : ILogger
{
    /// <summary>Fields the entry owns; a state key colliding with one of them is dropped.</summary>
    private static readonly HashSet<string> reservedFields = new(StringComparer.Ordinal)
    {
        "time", "level", "category", "eventId", "message", "exception"
    };

    /// <summary>Key under which the logging infrastructure passes the unrendered message template.</summary>
    private const string originalFormatKey = "{OriginalFormat}";

    private static CloudflareJsonContext json => CloudflareJsonContext.Default;

    /// <summary>
    /// Scopes are not projected. Nothing in the worker opens one, and a scope stack would have to
    /// live in an AsyncLocal to survive the await points every handler has.
    /// </summary>
    public IDisposable? BeginScope<TState> (TState state) where TState : notnull => null;

    public bool IsEnabled (LogLevel level) => level != LogLevel.None;

    public void Log<TState> (LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        sink.Write((int)level, BuildEntry(level, eventId, state, exception, formatter(state, exception)));
    }

    private string BuildEntry<TState> (LogLevel level, EventId eventId, TState state,
        Exception? exception, string message)
    {
        var entry = new Dictionary<string, JsonElement>
        {
            ["time"] = Text(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ["level"] = Text(LevelName(level)),
            ["category"] = Text(category),
        };
        if (eventId.Id != 0) entry["eventId"] = JsonSerializer.SerializeToElement(eventId.Id, json.Int32);
        entry["message"] = Text(message);
        if (exception is not null) entry["exception"] = Text(exception.ToString());
        AppendState(entry, state);
        return JsonSerializer.Serialize(entry, json.DictionaryStringJsonElement);
    }

    /// <summary>
    /// Flattens the message template's arguments onto the entry. Carrying them as fields of their
    /// own is the point of structured logging here: the log index can filter on a field, but not on
    /// a value baked into the rendered message.
    /// </summary>
    private static void AppendState<TState> (Dictionary<string, JsonElement> entry, TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs) return;
        foreach (var (key, value) in pairs)
        {
            // Duplicate keys leave the parsed object ambiguous, so the entry's own fields win.
            if (key == originalFormatKey || reservedFields.Contains(key)) continue;
            entry[key] = Value(value);
        }
    }

    private static JsonElement Value (object? value) => value switch
    {
        null => JsonSerializer.SerializeToElement((string?)null, json.String),
        bool flag => JsonSerializer.SerializeToElement(flag, json.Boolean),
        string text => Text(text),
        int number => JsonSerializer.SerializeToElement(number, json.Int32),
        long number => JsonSerializer.SerializeToElement(number, json.Int64),
        // Numbers stay JSON numbers so the log index can range-query them; NaN and the infinities
        // have no JSON literal and fall through to their text form.
        double number when double.IsFinite(number) => JsonSerializer.SerializeToElement(number, json.Double),
        float number when float.IsFinite(number) => JsonSerializer.SerializeToElement((double)number, json.Double),
        IFormattable number when IsInteger(value) =>
            JsonSerializer.Deserialize(number.ToString(null, CultureInfo.InvariantCulture), json.JsonElement),
        _ => Text(value.ToString())
    };

    private static JsonElement Text (string? value) => JsonSerializer.SerializeToElement(value, json.String);

    private static bool IsInteger (object value) => value is
        byte or sbyte or short or ushort or uint or ulong or decimal;

    private static string LevelName (LogLevel level) => level switch {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "information",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "none"
    };
}
