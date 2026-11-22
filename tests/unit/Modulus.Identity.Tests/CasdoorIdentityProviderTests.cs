using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Modulus.Identity.Casdoor;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class CasdoorIdentityProviderTests
{
    private const string SampleJson = """
        {
          "status": "ok",
          "data": {
            "owner": "built-in",
            "name": "alice",
            "id": "7c1b2a3e-0000-4000-8000-000000000001",
            "displayName": "Alice Example",
            "firstName": "Alice",
            "lastName": "Example",
            "email": "alice@example.com",
            "avatar": "https://door.example.com/avatar/alice.png",
            "isAdmin": false
          }
        }
        """;

    private static CasdoorIdentityProvider CreateProvider(StubHandler handler, out HttpClient client)
    {
        client = new HttpClient(handler);
        return new CasdoorIdentityProvider(client, new CasdoorOptions
        {
            Authority = "https://door.example.com/",
            ClientId = "modulus-app",
            ClientSecret = "secret",
        });
    }

    [Fact]
    public async Task GetUserBySubject_MapsStandardFields()
    {
        var provider = CreateProvider(new StubHandler(SampleJson), out _);

        var user = await provider.GetUserBySubjectAsync("7c1b2a3e-0000-4000-8000-000000000001");

        user.Should().NotBeNull();
        user!.Subject.Should().Be("7c1b2a3e-0000-4000-8000-000000000001");
        user.UserName.Should().Be("alice");
        user.Email.Should().Be("alice@example.com");
        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Example");
        user.AvatarUrl.Should().Be("https://door.example.com/avatar/alice.png");
        user.Claims["owner"].Should().Be("built-in");
    }

    [Theory]
    [InlineData("""{ "status": "error", "msg": "not found" }""")]
    [InlineData("""{ "status": "ok", "data": null }""")]
    [InlineData("""{ "status": "ok", "data": { "name": "ghost" } }""")]
    public async Task GetUserBySubject_FailedOrIdlessPayload_ReturnsNull(string body)
    {
        var provider = CreateProvider(new StubHandler(body), out _);

        (await provider.GetUserBySubjectAsync("x")).Should().BeNull();
    }

    [Fact]
    public async Task GetUserBySubject_SendsBasicAuthPerRequest_AndHitsExpectedUrl()
    {
        var handler = new StubHandler(SampleJson);
        var provider = CreateProvider(handler, out var client);

        await provider.GetUserBySubjectAsync("abc 1");

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://door.example.com/api/get-user?userId=abc%201"));
        var auth = handler.LastRequest.Headers.Authorization!;
        auth.Scheme.Should().Be("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)).Should().Be("modulus-app:secret");
    }

    [Fact]
    public void MetadataAddress_UsesGlobalDocument_OrTheNamedApplication()
    {
        CasdoorExtensions_MetadataAddress(null).Should().Be("https://door.example.com/.well-known/openid-configuration");
        CasdoorExtensions_MetadataAddress("shop").Should().Be("https://door.example.com/.well-known/shop/openid-configuration");
    }

    // MetadataAddress is internal; exercised through the provider's public registration surface is not possible
    // without a network, so the test assembly reaches it through InternalsVisibleTo where granted.
    private static string CasdoorExtensions_MetadataAddress(string? application)
        => CasdoorExtensions.MetadataAddress(new CasdoorOptions
        {
            Authority = "https://door.example.com/",
            ClientId = "c",
            ClientSecret = "s",
            Application = application,
        });

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
