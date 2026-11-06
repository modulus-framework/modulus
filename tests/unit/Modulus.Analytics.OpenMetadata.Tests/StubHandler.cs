namespace Modulus.Analytics.OpenMetadata.Tests;

using System.Net;

/// <summary>Answers requests from a queue of responses and records what was sent.</summary>
internal sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    public List<SentRequest> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Sent.Add(new SentRequest(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    internal sealed record SentRequest(HttpMethod Method, string Path, string? Authorization, string Body);

    public static HttpResponseMessage Json(HttpStatusCode code, string body = "{}") => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
