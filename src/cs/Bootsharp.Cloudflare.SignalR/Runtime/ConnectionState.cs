using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// Everything about one connection that must survive an isolate eviction, as it is written into the
/// socket's hibernation attachment. The connection id itself is NOT here: it is the socket's first
/// accept tag, which workerd makes immutable and indexes (<c>getWebSockets(tag)</c>).
/// </summary>
/// <remarks>
/// <para>Serialized through <see cref="SignalRJsonContext"/> onto a four-field DTO whose short
/// names are the on-the-wire contract. The payload types the app contributes go through the real
/// <c>JsonHubProtocol</c> instead, which is where a resolver belongs.</para>
/// <para>workerd caps the serialized attachment at 16 KiB (<c>web-socket.h:776</c>). Group names
/// are the only unbounded contributor, which <see cref="GroupLimit"/> bounds.</para>
/// </remarks>
internal sealed class ConnectionState
{
    /// <summary>
    /// Groups one connection may join. Upstream has no such limit because its group list is a
    /// process-lifetime object; here it is 16 KiB of attachment shared with everything else, and a
    /// silent truncation at the workerd boundary would lose group membership without a trace.
    /// </summary>
    public const int GroupLimit = 64;

    /// <summary>
    /// Whether the handshake completed. The JS client routes the very first frame it receives to
    /// its handshake parser unconditionally (<c>HubConnection.ts:647-655</c>), so a broadcast that
    /// raced ahead of the response would break the connection rather than arrive early — every
    /// send path filters on this.
    /// </summary>
    public bool Handshaken { get; set; }

    /// <summary>Result of the app's user-id resolution, or null for an anonymous connection.</summary>
    public string? UserIdentifier { get; set; }

    /// <summary>
    /// Group membership. A list rather than a set: membership is small, ordered output keeps the
    /// attachment byte-stable across rewrites, and <see cref="AddGroup"/> already de-duplicates.
    /// </summary>
    public List<string> Groups { get; } = [];

    /// <summary>When the connection was accepted, epoch milliseconds; the pre-handshake deadline.</summary>
    public double AcceptedAt { get; set; }

    public bool AddGroup (string group)
    {
        if (Groups.Contains(group, StringComparer.Ordinal)) return false;
        if (Groups.Count >= GroupLimit)
            throw new InvalidOperationException(
                $"Connection is already in {GroupLimit} groups, which is the per-connection limit " +
                "imposed by the 16 KiB Durable Object WebSocket attachment.");
        Groups.Add(group);
        return true;
    }

    public bool RemoveGroup (string group) => Groups.Remove(group);

    public bool InGroup (string group) => Groups.Contains(group, StringComparer.Ordinal);

    public string Serialize () =>
        JsonSerializer.Serialize(
            new ConnectionStateDto(Handshaken, AcceptedAt, UserIdentifier, Groups.Count == 0 ? null : Groups),
            SignalRJsonContext.Default.ConnectionStateDto);

    /// <summary>
    /// Reads an attachment back. A socket accepted by an older build, or one whose attachment was
    /// never written, reads as a fresh pre-handshake state rather than as an error: the alternative
    /// is a Durable Object that cannot start after a deploy.
    /// </summary>
    public static ConnectionState Deserialize (string? attachment)
    {
        var state = new ConnectionState();
        if (string.IsNullOrEmpty(attachment)) return state;
        ConnectionStateDto? dto;
        try { dto = JsonSerializer.Deserialize(attachment, SignalRJsonContext.Default.ConnectionStateDto); }
        catch (JsonException) { return state; }
        if (dto is null) return state;
        state.Handshaken = dto.Handshaken;
        state.AcceptedAt = dto.AcceptedAt;
        state.UserIdentifier = dto.UserIdentifier;
        if (dto.Groups is { Count: > 0 } groups) state.Groups.AddRange(groups);
        return state;
    }
}
