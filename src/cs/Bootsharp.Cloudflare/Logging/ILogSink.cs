namespace Bootsharp.Cloudflare.Logging;

/// <summary>
/// JavaScript side of the logging boundary, bound by the generated worker module before boot.
/// </summary>
/// <remarks>
/// Cloudflare's Workers Logs indexes a log line's fields only when a real JS object reaches
/// <c>console.*</c>; a JSON string arrives as one opaque message, and so does anything written to
/// stdout from WASM. Entries therefore cross as text and are parsed in the isolate, which is the
/// only side that can produce that object.
/// </remarks>
public interface ILogSink
{
    /// <param name="level">The <c>LogLevel</c> value; selects which <c>console</c> method is used.</param>
    /// <param name="entryJson">One flat JSON object carrying the entry's fields.</param>
    void Write(int level, string entryJson);
}
