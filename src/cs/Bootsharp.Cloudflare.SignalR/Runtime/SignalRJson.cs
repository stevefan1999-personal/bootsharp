using System.Text.Json.Serialization;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// Closed-world JSON for the SignalR host: negotiate payloads and the hibernation attachment.
/// Reflection-based serialization is off, so every type that crosses a JSON boundary is listed
/// here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NegotiateResponseView))]
[JsonSerializable(typeof(NegotiateErrorView))]
[JsonSerializable(typeof(NegotiateRedirectView))]
[JsonSerializable(typeof(ConnectionStateDto))]
internal sealed partial class SignalRJsonContext : JsonSerializerContext;

/// <summary>The four fields <c>NegotiateProtocol</c> reads.</summary>
internal sealed record NegotiateResponseView (
    int NegotiateVersion,
    string ConnectionId,
    string ConnectionToken,
    NegotiateTransportView[] AvailableTransports);

internal sealed record NegotiateTransportView (string Transport, string[] TransferFormats);

internal sealed record NegotiateErrorView (string Error);

internal sealed record NegotiateRedirectView (string Url, string? AccessToken);

/// <summary>
/// The attachment wire shape. Short names are load-bearing: workerd caps the value at 16 KiB
/// and existing sockets already store this document.
/// </summary>
internal sealed record ConnectionStateDto (
    [property: JsonPropertyName("h")] bool Handshaken,
    [property: JsonPropertyName("t")] double AcceptedAt,
    [property: JsonPropertyName("u")] string? UserIdentifier,
    [property: JsonPropertyName("g")] List<string>? Groups);