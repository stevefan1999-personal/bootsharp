using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// Everything about one connection that must survive an isolate eviction, as it is written into the
/// socket's hibernation attachment. The connection id itself is NOT here: it is the socket's first
/// accept tag, which workerd makes immutable and indexes (<c>getWebSockets(tag)</c>).
/// </summary>
/// <remarks>
/// <para>Read and written with a hand-rolled reader/writer rather than a serializer. The shape is
/// fixed and library-owned, so a <c>JsonSerializerContext</c> would be a generated type and a
/// package dependency to express six fields; the payload types the app contributes go through the
/// real <c>JsonHubProtocol</c> instead, which is where a resolver belongs.</para>
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

    public string Serialize ()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("h", Handshaken);
            writer.WriteNumber("t", AcceptedAt);
            if (UserIdentifier is not null) writer.WriteString("u", UserIdentifier);
            if (Groups.Count > 0)
            {
                writer.WriteStartArray("g");
                foreach (var group in Groups) writer.WriteStringValue(group);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Reads an attachment back. A socket accepted by an older build, or one whose attachment was
    /// never written, reads as a fresh pre-handshake state rather than as an error: the alternative
    /// is a Durable Object that cannot start after a deploy.
    /// </summary>
    public static ConnectionState Deserialize (string? attachment)
    {
        var state = new ConnectionState();
        if (string.IsNullOrEmpty(attachment)) return state;
        using var document = JsonDocument.Parse(attachment);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return state;
        if (root.TryGetProperty("h", out var handshaken)) state.Handshaken = handshaken.GetBoolean();
        if (root.TryGetProperty("t", out var accepted)) state.AcceptedAt = accepted.GetDouble();
        if (root.TryGetProperty("u", out var user)) state.UserIdentifier = user.GetString();
        if (!root.TryGetProperty("g", out var groups) || groups.ValueKind != JsonValueKind.Array) return state;
        foreach (var group in groups.EnumerateArray())
            if (group.GetString() is { } name)
                state.Groups.Add(name);
        return state;
    }
}
