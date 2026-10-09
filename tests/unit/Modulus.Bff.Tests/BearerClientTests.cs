namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

[Trait("Category", "Unit")]
public sealed class BearerClientTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Bff:Clients:mobile:ClientId"] = "shop-mobile",
        ["Bff:Clients:mobile:RequiredScopes:0"] = "api",
        ["Bff:Clients:mobile:MinimumAppVersion"] = "2.0",
        ["Bff:Clients:mobile:MinimumAppVersionByPlatform:ios"] = "2.5.0",
        ["Bff:Clients:mobile:RemoteApis:0:LocalPath"] = "/api/catalog",
        ["Bff:Clients:partner:PathPrefix"] = "/partner",
        ["Bff:Clients:partner:ClientId"] = "acme",
        ["Bff:Clients:partner:RequireIdempotencyKey"] = "true",
        ["Bff:Clients:partner:RemoteApis:0:LocalPath"] = "/api/orders",
    };

    private static Task<BffTestHost> StartAsync(Dictionary<string, string?>? extra = null)
    {
        var settings = Settings();
        foreach (var (k, v) in extra ?? [])
            settings[k] = v;
        return BffTestHost.StartAsync(settings, bff => bff.AddMobileClient().AddPartnerClient());
    }

    private static HttpRequestMessage Get(string url, string? token, string? version = null, string? platform = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (version is not null)
            request.Headers.Add(BffDefaults.AppVersionHeader, version);
        if (platform is not null)
            request.Headers.Add(BffDefaults.AppPlatformHeader, platform);
        return request;
    }

    [Fact]
    public async Task Mobile_bearer_is_forwarded_to_the_api()
    {
        await using var host = await StartAsync();
        var token = BffTestHost.CreateJwt("shop-mobile");

        var response = await host.Client().SendAsync(Get("/api/catalog/items", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<EchoResponse>();
        echo!.Authorization.Should().Be("Bearer " + token);
        echo.ClientApp.Should().Be("mobile");
    }

    [Fact]
    public async Task Me_returns_the_callers_claims()
    {
        await using var host = await StartAsync();
        var response = await host.Client().SendAsync(Get("/bff/me", BffTestHost.CreateJwt("shop-mobile")));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<BffUserClaim[]>()).Should().Contain(new BffUserClaim("sub", "bob"));
    }

    [Fact]
    public async Task Missing_or_invalid_token_gets_401()
    {
        await using var host = await StartAsync();
        (await host.Client().SendAsync(Get("/api/catalog/items", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client().SendAsync(Get("/api/catalog/items", "not-a-jwt"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_issued_to_another_client_is_forbidden()
    {
        await using var host = await StartAsync();
        var response = await host.Client().SendAsync(Get("/api/catalog/items", BffTestHost.CreateJwt("acme")));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_without_the_required_scope_is_forbidden()
    {
        await using var host = await StartAsync();
        var response = await host.Client().SendAsync(Get("/api/catalog/items", BffTestHost.CreateJwt("shop-mobile", scope: "openid")));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("1.9.9", null, HttpStatusCode.UpgradeRequired)]
    [InlineData("2.0", null, HttpStatusCode.OK)]
    [InlineData("2.4.0", "ios", HttpStatusCode.UpgradeRequired)]
    [InlineData("2.5.0-beta+7", "ios", HttpStatusCode.OK)]
    [InlineData("2.1", "android", HttpStatusCode.OK)]
    public async Task Old_app_versions_get_426(string version, string? platform, HttpStatusCode expected)
    {
        await using var host = await StartAsync();
        var response = await host.Client().SendAsync(Get("/api/catalog/items", BffTestHost.CreateJwt("shop-mobile"), version, platform));
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Unchanged_json_answers_304_to_a_matching_etag()
    {
        await using var host = await StartAsync();
        var token = BffTestHost.CreateJwt("shop-mobile");

        var first = await host.Client().SendAsync(Get("/api/catalog/items", token));
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();
        etag!.IsWeak.Should().BeTrue();

        var conditional = Get("/api/catalog/items", token);
        conditional.Headers.IfNoneMatch.Add(etag);
        var second = await host.Client().SendAsync(conditional);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_response_past_the_buffer_cap_streams_through_without_an_etag()
    {
        await using var host = await StartAsync();
        var token = BffTestHost.CreateJwt("shop-mobile");

        // The stub upstream echoes the request path, so a long query makes a body above the 1 MiB ETag buffer.
        var response = await host.Client().SendAsync(Get("/api/catalog/items?pad=" + new string('x', 1_200_000), token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Length.Should().BeGreaterThan(1_100_000);
    }

    [Fact]
    public async Task Rate_limit_answers_429_and_cannot_be_escaped_by_rotating_the_device_header()
    {
        await using var host = await StartAsync(new()
        {
            ["Bff:Clients:mobile:RateLimit:PermitLimit"] = "2",
            ["Bff:Clients:mobile:RateLimit:Window"] = "00:01:00",
        });
        var token = BffTestHost.CreateJwt("shop-mobile");

        async Task<HttpStatusCode> Call(string device)
        {
            var request = Get("/api/catalog/items", token);
            request.Headers.Add(BffDefaults.DeviceIdHeader, device);
            return (await host.Client().SendAsync(request)).StatusCode;
        }

        (await Call("phone-a")).Should().Be(HttpStatusCode.OK);
        (await Call("phone-a")).Should().Be(HttpStatusCode.OK);
        (await Call("phone-a")).Should().Be(HttpStatusCode.TooManyRequests);
        (await Call("phone-b")).Should().Be(HttpStatusCode.TooManyRequests, "the caller chooses X-Device-Id, so it cannot pick the partition");
        (await Call(Guid.NewGuid().ToString())).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Partner_writes_need_an_idempotency_key()
    {
        await using var host = await StartAsync();
        var token = BffTestHost.CreateJwt("acme");

        using var without = new HttpRequestMessage(HttpMethod.Post, "/partner/api/orders") { Content = JsonContent.Create(new { }) };
        without.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await host.Client().SendAsync(without)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var with = new HttpRequestMessage(HttpMethod.Post, "/partner/api/orders") { Content = JsonContent.Create(new { }) };
        with.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        with.Headers.Add("Idempotency-Key", "k-1");
        (await host.Client().SendAsync(with)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mobile_token_does_not_pass_the_partner_policy()
    {
        await using var host = await StartAsync();
        var response = await host.Client().SendAsync(Get("/partner/api/orders", BffTestHost.CreateJwt("shop-mobile")));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Aggregator_endpoints_get_the_client_policy_and_rules()
    {
        await using var host = await BffTestHost.StartAsync(Settings(), bff => bff.AddMobileClient(), app =>
            app.MapBffClient("mobile").MapGet("/home", (IBffClientContext client) => Results.Ok(new { client = client.Name })));

        (await host.Client().SendAsync(Get("/home", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client().SendAsync(Get("/home", BffTestHost.CreateJwt("shop-mobile"), "1.0"))).StatusCode.Should().Be(HttpStatusCode.UpgradeRequired);
        var ok = await host.Client().SendAsync(Get("/home", BffTestHost.CreateJwt("shop-mobile")));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Contain("\"mobile\"");
    }

    [Fact]
    public async Task Clients_sharing_a_host_need_distinct_prefixes()
    {
        var settings = Settings();
        settings.Remove("Bff:Clients:partner:PathPrefix");
        var act = () => BffTestHost.StartAsync(settings, bff => bff.AddMobileClient().AddPartnerClient());
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("share the path prefix");
    }
}
