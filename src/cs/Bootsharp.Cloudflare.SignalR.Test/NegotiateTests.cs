using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The negotiate response, whose field names are fixed by upstream's <c>NegotiateProtocol</c> and
/// whose one forbidden field is <c>useStatefulReconnect</c>: a client that did not ask for it
/// rejects the whole connection (<c>HttpConnection.ts:358-360</c>), and stateful reconnect is
/// SKIPped.
/// </summary>
public class NegotiateTests
{
    [Fact]
    public void ResponseCarriesTheFourFieldsTheClientReads ()
    {
        var root = JsonDocument.Parse(Negotiate.Response("abc")).RootElement;
        Assert.Equal(1, root.GetProperty("negotiateVersion").GetInt32());
        Assert.Equal("abc", root.GetProperty("connectionId").GetString());
        Assert.Equal("abc", root.GetProperty("connectionToken").GetString());
        var transport = root.GetProperty("availableTransports").EnumerateArray().Single();
        Assert.Equal("WebSockets", transport.GetProperty("transport").GetString());
        Assert.Equal("Text", transport.GetProperty("transferFormats").EnumerateArray().Single().GetString());
    }

    [Fact]
    public void UseStatefulReconnectIsNeverEmitted ()
    {
        Assert.DoesNotContain("useStatefulReconnect", Negotiate.Response("abc", "token"));
    }

    [Fact]
    public void ConnectionTokenCarriesTheRoutingKeyWhenTheAppSuppliesOne ()
    {
        var root = JsonDocument.Parse(Negotiate.Response("abc", "room:42")).RootElement;
        Assert.Equal("abc", root.GetProperty("connectionId").GetString());
        Assert.Equal("room:42", root.GetProperty("connectionToken").GetString());
    }

    [Fact]
    public void ErrorIsTheOnlyFieldOfAFailure ()
    {
        var root = JsonDocument.Parse(Negotiate.Error("no")).RootElement;
        Assert.Equal("no", root.GetProperty("error").GetString());
        Assert.Single(root.EnumerateObject());
    }

    [Fact]
    public void RedirectOmitsTheAccessTokenWhenThereIsNone ()
    {
        var root = JsonDocument.Parse(Negotiate.Redirect("https://other.example/hub")).RootElement;
        Assert.Equal("https://other.example/hub", root.GetProperty("url").GetString());
        Assert.False(root.TryGetProperty("accessToken", out _));
    }

    [Theory]
    [InlineData("/hub", "/hub/negotiate", true)]
    [InlineData("/hub/", "/hub/negotiate", true)]
    [InlineData("/hub", "/hub", false)]
    [InlineData("/hub", "/other/negotiate", false)]
    public void NegotiatePathIsRecognisedUnderEitherSpelling (string hubPath, string path, bool expected)
    {
        Assert.Equal(expected, Negotiate.IsNegotiate(path, hubPath));
    }
}
