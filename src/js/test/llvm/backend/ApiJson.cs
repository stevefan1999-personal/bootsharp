using System.Text.Json.Serialization;

namespace Llvm.Exercise;

public sealed record ErrorView(string Error);

public sealed record ProbeReport(
    int Invocation,
    int AwaitedInt,
    string AwaitedIntType,
    bool IsolateHandleHeld,
    bool IsolateHandleSameProxy,
    bool IsolateHandleUsable,
    string? IsolateHandleError,
    bool InvocationHandleSameProxy,
    bool StaleInvocationHandleThrew,
    string? StaleInvocationHandleError,
    string Runtime);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorView))]
[JsonSerializable(typeof(ProbeReport))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;