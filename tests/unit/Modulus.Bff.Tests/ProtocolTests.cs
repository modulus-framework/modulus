namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ProtocolTests
{
    [Fact]
    public async Task Oidc_login_challenges_with_code_pkce_and_extra_parameters()
    {
        await using var host = await BffTestHost.StartAsync(new()
        {
            ["Bff:AuthServer"] = "Auth0",
            ["Bff:Clients:web:ClientId"] = "shop-web",
            ["Bff:Clients:web:ClientSecret"] = "s3cret",
            ["Bff:Clients:web:AuthorizationParameters:audience"] = "https://shop-api",
        }, bff => bff.AddWebClient());

        var response = await host.Client().GetAsync("/bff/login?returnUrl=/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be("https://auth/connect/authorize");
        var query = QueryHelpers.ParseQuery(location.Query);
        query["response_type"].ToString().Should().Be("code");
        query["client_id"].ToString().Should().Be("shop-web");
        query["code_challenge_method"].ToString().Should().Be("S256");
        query["audience"].ToString().Should().Be("https://shop-api");
        query["scope"].ToString().Should().Contain("offline_access");
        query["redirect_uri"].ToString().Should().EndWith("/signin-oidc");
    }

    [Fact]
    public async Task Introspection_validates_opaque_tokens_and_caches_the_result()
    {
        await using var host = await BffTestHost.StartAsync(new()
        {
            ["Bff:AuthServer"] = "Okta",
            ["Bff:Clients:mobile:ClientId"] = "shop-mobile",
            ["Bff:Clients:mobile:ClientSecret"] = "s3cret",
            ["Bff:Clients:mobile:TokenValidation"] = "Introspection",
            ["Bff:Clients:mobile:RequiredScopes:0"] = "orders",
        }, bff => bff.AddMobileClient());

        async Task<HttpResponseMessage> Me(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await host.Client().SendAsync(request);
        }

        var first = await Me("opaque-1");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var claims = await first.Content.ReadFromJsonAsync<BffUserClaim[]>();
        claims.Should().Contain(new BffUserClaim("sub", "carol"));
        claims.Should().Contain(new BffUserClaim("role", "ops"), "Okta groups are normalized to roles");

        (await Me("opaque-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Auth.IntrospectionCalls.Should().Be(1, "the active result is cached");

        (await Me("revoked")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
