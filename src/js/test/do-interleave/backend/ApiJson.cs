using System.Text.Json.Serialization;

namespace Interleave.Harness;

public sealed record TraceEntry(int Step, long At, string Event, string Detail, int Depth);

public sealed record InterleaveReport(
    long IsolateBornAt,
    int Constructions,
    int Actor,
    int PeakDepth,
    int Depth,
    string Runtime,
    Dictionary<string, int> Conversations,
    TraceEntry[] Trace);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InterleaveReport))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;