using System.Text.Json;
using NekoChat.Core.Http;

namespace NekoChat.Core.Kick;

/// <summary>Fetches JSON from Kick's unofficial API — see <see cref="CurlHttpClient"/> for why this goes through curl.exe rather than HttpClient.</summary>
internal static class KickApiClient
{
    public static async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken ct)
    {
        var stdout = await CurlHttpClient.GetStringAsync(url, ct, CurlHttpClient.BrowserUserAgent);
        return JsonDocument.Parse(stdout);
    }
}
