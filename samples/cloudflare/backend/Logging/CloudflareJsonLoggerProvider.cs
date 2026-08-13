using Microsoft.Extensions.Logging;

namespace Cloudflare.Backend.Logging;

/// <summary>
/// The worker's only logging provider: every category writes through the same JS sink, which is
/// isolate-wide and stateless, so nothing here needs disposing.
/// </summary>
internal sealed class CloudflareJsonLoggerProvider(ILogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CloudflareJsonLogger(categoryName, sink);

    public void Dispose() { }
}
