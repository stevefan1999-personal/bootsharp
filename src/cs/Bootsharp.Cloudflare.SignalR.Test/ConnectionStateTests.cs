namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The attachment codec. It is the only thing about a connection that survives an isolate eviction,
/// and workerd caps the serialized attachment at 16 KiB — so both the round trip and the bound on
/// its one unbounded contributor are load-bearing.
/// </summary>
public class ConnectionStateTests
{
    [Fact]
    public void RoundTripsEveryField ()
    {
        var state = new ConnectionState { Handshaken = true, UserIdentifier = "u1", AcceptedAt = 1234.5 };
        state.AddGroup("a");
        state.AddGroup("b");
        var read = ConnectionState.Deserialize(state.Serialize());
        Assert.True(read.Handshaken);
        Assert.Equal("u1", read.UserIdentifier);
        Assert.Equal(1234.5, read.AcceptedAt);
        Assert.Equal(["a", "b"], read.Groups);
    }

    [Fact]
    public void SerializationIsByteStableAcrossRewrites ()
    {
        // Group order is preserved so an unchanged state does not rewrite the attachment with
        // different bytes on every send path that touches it.
        var state = new ConnectionState { Handshaken = true, AcceptedAt = 1 };
        state.AddGroup("b");
        state.AddGroup("a");
        Assert.Equal(state.Serialize(), ConnectionState.Deserialize(state.Serialize()).Serialize());
    }

    [Fact]
    public void OmitsTheAbsentOptionalFields ()
    {
        var json = new ConnectionState { AcceptedAt = 1 }.Serialize();
        Assert.DoesNotContain("\"u\"", json);
        Assert.DoesNotContain("\"g\"", json);
    }

    [Fact]
    public void AMissingAttachmentReadsAsAFreshPreHandshakeState ()
    {
        // A socket accepted by an older build, or one whose attachment was never written, must not
        // fail the Durable Object — the alternative is an actor that cannot start after a deploy.
        var state = ConnectionState.Deserialize(null);
        Assert.False(state.Handshaken);
        Assert.Empty(state.Groups);
    }

    [Fact]
    public void AnAttachmentThatIsNotAnObjectReadsAsAFreshState ()
    {
        Assert.False(ConnectionState.Deserialize("[1,2,3]").Handshaken);
    }

    [Fact]
    public void AddingAGroupTwiceIsANoOp ()
    {
        var state = new ConnectionState();
        Assert.True(state.AddGroup("room"));
        Assert.False(state.AddGroup("room"));
        Assert.Single(state.Groups);
    }

    [Fact]
    public void TheSixtyFifthGroupIsRefusedRatherThanSilentlyTruncated ()
    {
        var state = new ConnectionState();
        for (var index = 0; index < ConnectionState.GroupLimit; index++)
            Assert.True(state.AddGroup($"room{index}"));
        var error = Assert.Throws<InvalidOperationException>(() => state.AddGroup("one too many"));
        Assert.Contains("16 KiB", error.Message);
    }

    [Fact]
    public void GroupMatchingIsOrdinalRatherThanCaseInsensitive ()
    {
        // Upstream's HubGroupList is keyed with StringComparer.Ordinal; "Room" and "room" are two
        // groups there and must be two groups here.
        var state = new ConnectionState();
        state.AddGroup("Room");
        Assert.False(state.InGroup("room"));
        Assert.True(state.InGroup("Room"));
    }
}
