namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Bff.Authentication;
using Modulus.Bff.Middleware;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GatewayTests
{
    private static Task<BffTestHost> StartGatewayAsync(Action<WebApplication>? map = null) => BffTestHost.StartAsync(new()
    {
        ["Bff:Clients:web:PathPrefix"] = "/web",
        ["Bff:Clients:web:LoginMode"] = "Password",
        ["Bff:Clients:web:ClientId"] = "shop-web",
        ["Bff:Clients:web:RemoteApis:0:LocalPath"] = "/api",
        ["Bff:Clients:mobile:PathPrefix"] = "/mobile",
        ["Bff:Clients:mobile:ClientId"] = "shop-mobile",
        ["Bff:Clients:mobile:RemoteApis:0:LocalPath"] = "/api",
    }, bff => bff.AddWebClient().AddMobileClient(), map);

    [Fact]
    public async Task Each_client_lives_under_its_prefix_with_its_own_auth()
    {
        await using var host = await StartGatewayAsync();
        var browser = new Browser(host.Client());
        (await browser.PostJsonAsync("/web/bff/login", new { userName = "alice", password = "pw" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var web = await (await browser.GetAsync("/web/api/items")).Content.ReadFromJsonAsync<EchoResponse>();
        web!.Path.Should().Be("/api/items");
        web.ClientApp.Should().Be("web");

        // The web cookie is not a credential for the mobile client.
        (await browser.GetAsync("/mobile/bff/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A mobile token is not a credential for the web client.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/web/bff/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BffTestHost.CreateJwt("shop-mobile"));
        request.Headers.Add("X-CSRF", "1");
        (await host.Client().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var mobile = new HttpRequestMessage(HttpMethod.Get, "/mobile/api/items");
        mobile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BffTestHost.CreateJwt("shop-mobile"));
        var echo = await (await host.Client().SendAsync(mobile)).Content.ReadFromJsonAsync<EchoResponse>();
        echo!.ClientApp.Should().Be("mobile");
    }

    [Fact]
    public async Task Typed_clients_carry_the_web_session_token()
    {
        await using var host = await BffTestHost.StartAsync(new()
        {
            ["Bff:Clients:web:LoginMode"] = "Password",
            ["Bff:Clients:web:ClientId"] = "shop-web",
        }, bff =>
        {
            bff.AddWebClient();
            bff.Services.AddBffApiClient<CatalogClient>();
        }, app => app.MapBffClient("web").MapGet("/home", async (CatalogClient catalog, BffComposer composer, CancellationToken ct) =>
        {
            var composition = composer.Begin(TimeSpan.FromSeconds(5));
            var items = composition.Required("catalog", catalog.GetAsync);
            var broken = composition.Optional("recommendations", catalog.FailAsync);
            return await composition.ExecuteAsync(() => new { items = items.Value, recommendations = broken.Value }, ct);
        }));

        var browser = new Browser(host.Client());
        await browser.PostJsonAsync("/bff/login", new { userName = "alice", password = "pw" });
        var response = await browser.GetAsync("/home");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Bearer at-1").And.Contain("\"clientApp\":\"web\"");
        body.Should().Contain("\"degraded\":[\"recommendations\"]");
    }

    internal sealed class CatalogClient(HttpClient http)
    {
        public async Task<EchoResponse?> GetAsync(CancellationToken ct) => await http.GetFromJsonAsync<EchoResponse>("catalog", ct);

        public async Task<EchoResponse?> FailAsync(CancellationToken ct)
        {
            using var response = await http.GetAsync("fail", ct);
            response.EnsureSuccessStatusCode();
            return null;
        }
    }
}

[Trait("Category", "Unit")]
public sealed class CompositionTests
{
    private static BffComposition Begin(TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IBffClientContext>(new NullClient());
        services.AddScoped<BffComposer>();
        return services.BuildServiceProvider().GetRequiredService<BffComposer>().Begin(timeout);
    }

    [Fact]
    public async Task Sections_run_concurrently()
    {
        var composition = Begin();
        var bothStarted = new TaskCompletionSource();
        var started = 0;
        async Task<int> Section(CancellationToken ct)
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.SetResult();
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return 1;
        }

        var a = composition.Required("a", Section);
        var b = composition.Required("b", Section);
        await composition.ExecuteAsync();

        a.Value.Should().Be(1);
        b.Value.Should().Be(1);
    }

    [Fact]
    public async Task Failing_optional_section_degrades()
    {
        var composition = Begin();
        var ok = composition.Required("ok", _ => Task.FromResult("fine"));
        var bad = composition.Optional<string>("orders", _ => throw new HttpRequestException("down"));

        var response = await composition.ExecuteAsync(() => new { ok = ok.Value, orders = bad.Value });

        response.Degraded.Should().Equal("orders");
        bad.Succeeded.Should().BeFalse();
        response.Data.ok.Should().Be("fine");
    }

    [Fact]
    public async Task Slow_optional_section_times_out()
    {
        var composition = Begin();
        var slow = composition.Optional("slow", async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return 1;
        }, new BffSectionOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        var started = DateTime.UtcNow;
        await composition.ExecuteAsync();

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
        composition.Degraded.Should().Equal("slow");
        slow.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Failing_required_section_throws()
    {
        var composition = Begin();
        composition.Required<int>("catalog", _ => throw new HttpRequestException("down"));
        var act = () => composition.ExecuteAsync();
        (await act.Should().ThrowAsync<BffSectionFailedException>()).Which.Section.Should().Be("catalog");
    }

    [Fact]
    public async Task Cached_sections_are_kept_apart_per_selected_company()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<Modulus.Caching.ICacheService, Modulus.Caching.MemoryCacheService>();
        services.AddHttpContextAccessor();
        services.AddSingleton<IBffClientContext>(new NullClient());
        services.AddScoped<BffComposer>();
        var sp = services.BuildServiceProvider();
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var calls = 0;

        async Task<string?> Home(string company)
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "same-user")], "test")),
            };
            context.Request.Headers["X-Tenant-Id"] = company;
            accessor.HttpContext = context;
            var composition = sp.GetRequiredService<BffComposer>().Begin();
            var section = composition.Optional("orders", _ => Task.FromResult($"{company}-{++calls}"),
                new BffSectionOptions { CacheDuration = TimeSpan.FromMinutes(5) });
            await composition.ExecuteAsync();
            return section.Value;
        }

        (await Home("company-a")).Should().Be("company-a-1");
        (await Home("company-b")).Should().Be("company-b-2", "one login in another company must not see the first one's cache");
        (await Home("company-a")).Should().Be("company-a-1");
    }

    private sealed class NullClient : IBffClientContext
    {
        public string? Name => "mobile";

        public BffClientKind? Kind => BffClientKind.Mobile;

        public BffClientOptions? Options => null;
    }
}

[Trait("Category", "Unit")]
public sealed class ClaimsNormalizationTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Keycloak_realm_roles_are_unpacked()
    {
        var p = BffClaims.Normalize(Principal(("sub", "1"), ("realm_access", "{\"roles\":[\"admin\",\"user\"]}")),
            BffClaims.DefaultRoleClaimTypes(BffAuthServer.Keycloak), "s");
        p.IsInRole("admin").Should().BeTrue();
        p.IsInRole("user").Should().BeTrue();
    }

    [Fact]
    public void Okta_groups_and_cid_are_recognized()
    {
        var p = BffClaims.Normalize(Principal(("cid", "0oa1"), ("groups", "Everyone"), ("groups", "Admins"), ("scp", "orders.read")),
            BffClaims.DefaultRoleClaimTypes(BffAuthServer.Okta), "s");
        p.IsInRole("Admins").Should().BeTrue();
        BffClaims.GetClientId(p).Should().Be("0oa1");
        BffClaims.GetScopes(p).Should().Contain("orders.read");
    }

    [Fact]
    public void Entra_roles_and_azp_are_recognized()
    {
        var p = BffClaims.Normalize(Principal(("azp", "app-guid"), ("roles", "Orders.Admin"), ("scp", "a b")),
            BffClaims.DefaultRoleClaimTypes(BffAuthServer.AzureAd), "s");
        p.IsInRole("Orders.Admin").Should().BeTrue();
        BffClaims.GetClientId(p).Should().Be("app-guid");
        BffClaims.GetScopes(p).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void Name_falls_back_to_preferred_username_then_email()
    {
        BffClaims.Normalize(Principal(("email", "a@b.c")), [], "s").Identity!.Name.Should().Be("a@b.c");
        BffClaims.Normalize(Principal(("preferred_username", "ann"), ("email", "a@b.c")), [], "s").Identity!.Name.Should().Be("ann");
    }

    [Fact]
    public void Configured_role_claim_types_are_read()
    {
        var p = BffClaims.Normalize(Principal(("https://shop/roles", "[\"buyer\"]")), ["https://shop/roles"], "s");
        p.IsInRole("buyer").Should().BeTrue();
    }
}

[Trait("Category", "Unit")]
public sealed class EdgeRuleTests
{
    [Theory]
    [InlineData("/orders", "/orders")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("https://evil.test", "/")]
    [InlineData("//evil.test", "/")]
    [InlineData("/\\evil.test", "/")]
    public void Return_urls_stay_local(string? input, string expected)
        => BffEndpointRouteBuilderExtensions.SafeReturnUrl(input).Should().Be(expected);

    [Theory]
    [InlineData("2", "2.0")]
    [InlineData("v2.3.1", "2.3.1")]
    [InlineData("2.3.1-beta.1+42", "2.3.1")]
    [InlineData("garbage", null)]
    public void App_versions_parse_leniently(string input, string? expected)
        => (BffMiddleware.ParseVersion(input)?.ToString()).Should().Be(expected);

    [Fact]
    public void Missing_app_version_passes_unless_required()
    {
        var context = new DefaultHttpContext();
        BffMiddleware.AppVersionAllowed(context, new BffClientOptions { MinimumAppVersion = "2.0" }, out _).Should().BeTrue();
        BffMiddleware.AppVersionAllowed(context, new BffClientOptions { MinimumAppVersion = "2.0", RequireAppVersion = true }, out _).Should().BeFalse();
    }
}
