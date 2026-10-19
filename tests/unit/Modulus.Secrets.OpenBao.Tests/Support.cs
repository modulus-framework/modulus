namespace Modulus.Secrets.OpenBao.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>One request the client sent, as the fake server saw it.</summary>
internal sealed record SentRequest(string Method, string Path, string? Token, string Body);

/// <summary>Answers each request through <paramref name="respond"/> and records what was sent.</summary>
internal sealed class ScriptedHandler(Func<SentRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<SentRequest> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues("X-Vault-Token", out var tokens);
        var sent = new SentRequest(request.Method.Method, request.RequestUri!.AbsolutePath, tokens?.FirstOrDefault(), body);
        Sent.Add(sent);
        return respond(sent);
    }
}

/// <summary>Canned responses.</summary>
internal static class Respond
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    public const string LoginLease900 = """{"auth":{"client_token":"tok-a","lease_duration":900,"renewable":true}}""";
}

/// <summary>A clock the test moves forward by hand.</summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A token or credential file that a test can rewrite.</summary>
internal sealed class TempFile : IDisposable
{
    public TempFile(string content)
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"modulus-openbao-{Guid.NewGuid():N}.txt");
        Write(content);
    }

    public string FilePath { get; }

    public void Write(string content) => File.WriteAllText(FilePath, content);

    public void Dispose()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}

/// <summary>Builds the client under test on top of the fake handler.</summary>
internal static class Harness
{
    public static OpenBaoOptions Options(OpenBaoAuthOptions auth) => new()
    {
        Address = "https://bao.test",
        Mount = "modulus",
        PathPrefix = "tenant-db",
        Auth = auth,
    };

    public static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://bao.test/") };

    public static OpenBaoTokenProvider Tokens(HttpClient http, OpenBaoAuthOptions auth, TimeProvider clock)
        => new(http, auth, clock, NullLogger.Instance);

    public static OpenBaoKvClient Kv(HttpMessageHandler handler, OpenBaoOptions options, TimeProvider clock)
    {
        var http = Client(handler);
        return new OpenBaoKvClient(http, Tokens(http, options.Auth, clock), options);
    }
}
