using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// The subset of upstream's <c>HubOptions</c> that still means something on this host, plus the one
/// setting that is new: the JSON type resolver the reused <c>JsonHubProtocol</c> serializes payloads
/// through.
/// </summary>
/// <remarks>
/// Dropped on purpose, each because the mechanism behind it is gone rather than because it was
/// inconvenient: <c>KeepAliveInterval</c> (workerd's auto-response answers the client's ping without
/// waking the actor, so the server never originates one), <c>MaximumParallelInvocationsPerClient</c>
/// (the per-connection FIFO <i>is</i> the upstream default of 1, and a value above 1 would reintroduce
/// exactly the reordering the FIFO exists to prevent), <c>SupportedProtocols</c> (JSON only),
/// <c>StreamBufferCapacity</c> and <c>MaximumReceiveMessageSize</c> (workerd frames the messages).
/// </remarks>
public sealed class HubOptions
{
    /// <summary>
    /// Whether exception detail from a failing hub method reaches the client. False by default,
    /// as upstream: the message of an arbitrary server exception is not a contract.
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// How long a socket may stay accepted without completing its handshake. Upstream's default is
    /// 15 s (<c>HubOptionsSetup.cs:15</c>); enforced by the alarm sweep rather than by a timer,
    /// because a timer would not survive hibernation.
    /// </summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a handshaken socket may go without a ping before the sweep closes it. Upstream's
    /// <c>ClientTimeoutInterval</c> default is 30 s; liveness is read from
    /// <c>getWebSocketAutoResponseTimestamp</c>, which the ping absorber stamps without waking us.
    /// </summary>
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Type resolver for hub method arguments and return values. Combined with
    /// <see cref="HubPrimitivesContext"/>, which covers the primitives every hub uses, so an app
    /// only declares a context for its own payload records.
    /// </summary>
    /// <remarks>
    /// This is the whole AOT story of the reused protocol. <c>JsonHubProtocol</c>'s only reflective
    /// lines are its two <c>object?</c>/<c>Type</c> <c>JsonSerializer</c> overloads
    /// (<c>JsonHubProtocol.cs:914-921</c>); with
    /// <c>JsonSerializerIsReflectionEnabledByDefault=false</c> they resolve <b>solely</b> through
    /// this resolver, which is why the shipped assembly is AOT-clean without being modified.
    /// </remarks>
    public IJsonTypeInfoResolver? PayloadTypeInfoResolver { get; set; }

    /// <summary>
    /// Builds the protocol this host speaks, preserving upstream's serializer defaults verbatim
    /// (<c>JsonHubProtocol.cs:923-939</c>) — camelCase names, case-insensitive reads, depth 64,
    /// relaxed escaping, no indentation. A client that works against ASP.NET Core sees the same
    /// bytes here.
    /// </summary>
    public IHubProtocol CreateProtocol ()
    {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            MaxDepth = 64,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            TypeInfoResolver = PayloadTypeInfoResolver is null
                ? HubPrimitivesContext.Default
                : JsonTypeInfoResolver.Combine(HubPrimitivesContext.Default, PayloadTypeInfoResolver)
        };
        return new JsonHubProtocol(Microsoft.Extensions.Options.Options.Create(
            new JsonHubProtocolOptions { PayloadSerializerOptions = options }));
    }
}

/// <summary>
/// The payload types every hub uses whether or not it declares any of its own. Shipped so that an
/// app with a <c>Send(string user, string message)</c> hub needs no serializer context at all, and
/// so that an app that does declare one only lists its own records.
/// </summary>
/// <remarks>
/// Roslyn source generators cannot chain — the JSON generator never sees a context another generator
/// emitted — so the hub generator reports the <c>[JsonSerializable]</c> declarations an app owes
/// (CFW040) instead of emitting them. This context is the half that can be shipped.
/// </remarks>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(double[]))]
public sealed partial class HubPrimitivesContext : JsonSerializerContext;
