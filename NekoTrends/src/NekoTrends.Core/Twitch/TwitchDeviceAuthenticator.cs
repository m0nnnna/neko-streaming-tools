using System.Text.Json;

namespace NekoTrends.Core.Twitch;

/// <summary>
/// OAuth Device Code Grant against id.twitch.tv — the flow meant for apps with no embedded
/// browser or local redirect server: the user visits a URL and types a short code instead of us
/// handling any redirect. See
/// https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/#device-code-grant-flow
///
/// NekoTrends only reads public stream listings, so it requests no scopes at all — the token
/// exists purely to satisfy Helix's authentication requirement.
/// </summary>
public sealed class TwitchDeviceAuthenticator
{
    private const string DeviceEndpoint = "https://id.twitch.tv/oauth2/device";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";

    private readonly HttpClient _http;
    private readonly string _clientId;

    public TwitchDeviceAuthenticator(string clientId, HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _http = httpClient ?? new HttpClient();
    }

    public async Task<DeviceCodeInfo> RequestDeviceCodeAsync(IReadOnlyList<string>? scopes = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scopes"] = string.Join(' ', scopes ?? []),
        };

        using var response = await _http.PostAsync(DeviceEndpoint, new FormUrlEncodedContent(body), ct);
        var root = await ParseJsonAsync(response, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch device code request failed ({response.StatusCode}): {root.GetRawText()}");

        return new DeviceCodeInfo(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.GetProperty("interval").GetInt32());
    }

    /// <summary>
    /// Polls until the user approves the device code on Twitch's site, the code expires, or
    /// cancellation is requested. Twitch responds 400 "authorization_pending" for every poll
    /// before approval — that's expected, not an error.
    /// </summary>
    public async Task<TwitchToken> PollForTokenAsync(DeviceCodeInfo deviceCode, IReadOnlyList<string>? scopes = null, CancellationToken ct = default)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(deviceCode.IntervalSeconds, 1));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresInSeconds);

        while (true)
        {
            await Task.Delay(interval, ct);

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Twitch device authorization expired before it was approved.");

            var body = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scopes"] = string.Join(' ', scopes ?? []),
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            };

            using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(body), ct);
            var root = await ParseJsonAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return ParseToken(root);

            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
            switch (message)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                default:
                    throw new InvalidOperationException($"Twitch device authorization failed: {message ?? root.GetRawText()}");
            }
        }
    }

    public async Task<TwitchToken> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(body), ct);
        var root = await ParseJsonAsync(response, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch token refresh failed ({response.StatusCode}): {root.GetRawText()}");

        return ParseToken(root);
    }

    private static TwitchToken ParseToken(JsonElement root)
    {
        var scopes = root.TryGetProperty("scope", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.Array
            ? scopeEl.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];

        return new TwitchToken(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            scopes);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}
