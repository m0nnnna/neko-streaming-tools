using System.Net;
using NekoChat.Core.Twitch;

namespace NekoChat.Core.Tests.Twitch;

public class TwitchDeviceAuthenticatorTests
{
    [Fact]
    public async Task RequestDeviceCodeAsync_ParsesResponse()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
            {"device_code":"abc123","user_code":"WXYZ-1234","verification_uri":"https://twitch.tv/activate","expires_in":1800,"interval":5}
            """);
        var auth = new TwitchDeviceAuthenticator("client123", new HttpClient(handler));

        var info = await auth.RequestDeviceCodeAsync(["user:read:chat"]);

        Assert.Equal("abc123", info.DeviceCode);
        Assert.Equal("WXYZ-1234", info.UserCode);
        Assert.Equal("https://twitch.tv/activate", info.VerificationUri);
        Assert.Equal(1800, info.ExpiresInSeconds);
        Assert.Equal(5, info.IntervalSeconds);
        Assert.Contains("client_id=client123", handler.RequestBodies[0]);
        Assert.Contains("scopes=user%3Aread%3Achat", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_ThrowsOnError()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, """{"status":400,"message":"invalid client"}""");
        var auth = new TwitchDeviceAuthenticator("bad-client", new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.RequestDeviceCodeAsync(["user:read:chat"]));
    }

    [Fact]
    public async Task PollForTokenAsync_KeepsPollingThroughAuthorizationPending()
    {
        var deviceCode = new DeviceCodeInfo("abc123", "WXYZ-1234", "https://twitch.tv/activate", 1800, 0);
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.BadRequest, """{"status":400,"message":"authorization_pending"}""")
            .Enqueue(HttpStatusCode.BadRequest, """{"status":400,"message":"authorization_pending"}""")
            .Enqueue(HttpStatusCode.OK, """
                {"access_token":"tok","refresh_token":"ref","expires_in":14400,"scope":["user:read:chat"],"token_type":"bearer"}
                """);
        var auth = new TwitchDeviceAuthenticator("client123", new HttpClient(handler));

        var token = await auth.PollForTokenAsync(deviceCode, ["user:read:chat"]);

        Assert.Equal("tok", token.AccessToken);
        Assert.Equal("ref", token.RefreshToken);
        Assert.Equal(14400, token.ExpiresInSeconds);
        Assert.Equal(3, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task PollForTokenAsync_ThrowsOnRealError()
    {
        var deviceCode = new DeviceCodeInfo("abc123", "WXYZ-1234", "https://twitch.tv/activate", 1800, 0);
        var handler = new FakeHttpMessageHandler()
            .Enqueue(HttpStatusCode.BadRequest, """{"status":400,"message":"access_denied"}""");
        var auth = new TwitchDeviceAuthenticator("client123", new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.PollForTokenAsync(deviceCode, ["user:read:chat"]));
    }

    [Fact]
    public async Task RefreshTokenAsync_ParsesResponse()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
            {"access_token":"newtok","refresh_token":"newref","expires_in":14400,"scope":["user:read:chat"],"token_type":"bearer"}
            """);
        var auth = new TwitchDeviceAuthenticator("client123", new HttpClient(handler));

        var token = await auth.RefreshTokenAsync("oldref");

        Assert.Equal("newtok", token.AccessToken);
        Assert.Equal("newref", token.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.RequestBodies[0]);
    }
}
