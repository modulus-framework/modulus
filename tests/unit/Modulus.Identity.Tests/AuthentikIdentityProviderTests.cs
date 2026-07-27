using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Modulus.Identity.Authentik;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class AuthentikIdentityProviderTests
{
    private const string SampleUserJson = """
        {
          "pk": 42,
          "username": "alice",
          "name": "Alice Example",
          "email": "alice@example.com",
          "avatar": "https://auth.example.com/avatar/42.png",
          "is_active": true,
          "groups": ["users", "admins"]
        }
        """;

    private static AuthentikIdentityProvider CreateProvider(
        StubHandler handler, out HttpClient capturedClient)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://auth.example.com/", UriKind.Absolute),
        };
        capturedClient = client;
        var opts = new AuthentikOptions
        {
            Authority = "https://auth.example.com/",
            ClientId = "modulus-app",
            ClientSecret = "secret",
            ApiToken = "api-token-123",
        };
        return new AuthentikIdentityProvider(client, opts);
    }

    [Fact]
    public async Task GetUserBySubject_MapsStandardFields()
    {
        var handler = StubHandler.Returns(SampleUserJson);
        var provider = CreateProvider(handler, out _);

        var user = await provider.GetUserBySubjectAsync("42");

        user.Should().NotBeNull();
        user!.Subject.Should().Be("42");
        user.Email.Should().Be("alice@example.com");
        user.UserName.Should().Be("alice");
        user.FirstName.Should().Be("Alice Example");
        user.LastName.Should().BeNull();
        user.AvatarUrl.Should().Be("https://auth.example.com/avatar/42.png");
        user.Claims.Should().ContainKey("username");
        user.Claims["username"].Should().Be("alice");
    }

    [Fact]
    public async Task GetUserBySubject_PayloadWithoutPkOrSub_ReturnsNull()
    {
        var handler = StubHandler.Returns("""{ "username": "ghost" }""");
        var provider = CreateProvider(handler, out _);

        var user = await provider.GetUserBySubjectAsync("anything");

        user.Should().BeNull();
    }

    /// <summary>
    /// Regression: previously the adapter wrote the bearer token to
    /// <c>HttpClient.DefaultRequestHeaders.Authorization</c>, mutating shared
    /// state across concurrent calls. The fix moves it to a per-request
    /// <see cref="HttpRequestMessage"/>. This test fails loudly if the
    /// regression returns.
    /// </summary>
    [Fact]
    public async Task GetUserBySubject_DoesNotMutateSharedHttpClientHeaders()
    {
        var handler = StubHandler.Returns(SampleUserJson);
        var provider = CreateProvider(handler, out var client);

        await provider.GetUserBySubjectAsync("42");

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    /// <summary>
    /// The bearer token must reach the upstream request (otherwise the
    /// regression fix would silently break Authentik's admin API auth).
    /// </summary>
    [Fact]
    public async Task GetUserBySubject_SetsBearerOnIndividualRequest()
    {
        var handler = StubHandler.Returns(SampleUserJson);
        var provider = CreateProvider(handler, out _);

        await provider.GetUserBySubjectAsync("42");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization
            .Should().BeOfType<AuthenticationHeaderValue>()
            .Which.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization!.Parameter
            .Should().Be("api-token-123");
    }

    [Fact]
    public async Task GetUserBySubject_HitsExpectedUrl()
    {
        var handler = StubHandler.Returns(SampleUserJson);
        var provider = CreateProvider(handler, out _);

        await provider.GetUserBySubjectAsync("42");

        handler.LastRequest!.RequestUri
            .Should().Be(new Uri("https://auth.example.com/api/v3/core/users/42/"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public HttpRequestMessage? LastRequest { get; private set; }

        private StubHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        public static StubHandler Returns(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(body, status);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            var content = new StringContent(_body);
            var resp = new HttpResponseMessage(_status) { Content = content };
            return Task.FromResult(resp);
        }
    }
}
