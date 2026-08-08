using System.Net;

namespace NekoChat.Core.Tests.Twitch;

/// <summary>Returns queued responses in order, one per request, for testing HttpClient-based code without real network calls.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<string> RequestBodies { get; } = new();

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string jsonBody)
    {
        _responses.Enqueue((status, jsonBody));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more fake responses queued.");

        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
