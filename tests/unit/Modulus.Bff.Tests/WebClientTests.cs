namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Modulus.Caching;
using Xunit;

[Trait("Category", "Unit")]
public sealed class WebClientTests
{
    private static Dictionary<string, string?> WebSettings(string storage = "ServerSide") => new()
    {
        ["Bff:AuthServer"] = "Keycloak",
        ["Bff:Clients:web:ClientId"] = "shop-web",
        ["Bff:Clients:web:ClientSecret"] = "s3cret",
        ["Bff:Clients:web:LoginMode"] = "Password",
        ["Bff:Clients:web:TokenStorage"] = storage,
        ["Bff:Clients:web:RemoteApis:0:LocalPath"] = "/api/catalog",
        ["Bff:Clients:web:RemoteApis:0:RemotePath"] = "/v1/catalog",
    };

    private static Task<BffTestHost> StartAsync(string storage = "ServerSide", bool withRevocation = true)
        => BffTestHost.StartAsync(WebSettings(storage), bff => bff.AddWebClient(), withRevocation: withRevocation);

    private static async Task<Browser> LoginAsync(BffTestHost host)
    {
        var browser = new Browser(host.Client());
        var login = await browser.PostJsonAsync("/bff/login", new { userName = "alice", password = "pw" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return browser;
    }

    [Fact]
    public async Task Password_login_opens_a_session_whose_cookie_holds_no_token()
    {
        await using var host = await StartAsync();
        var browser = await LoginAsync(host);

        browser.Cookie.Should().NotBeNull();
        browser.Cookie.Should().NotContain("at-1");
        host.Auth.LastTokenAuthorization.Should().StartWith("Basic ", "a confidential client authenticates with client_secret_basic");
        host.Auth.LastTokenForm!.Should().NotContainKey("client_secret");

        var user = await browser.GetAsync("/bff/user");
        user.StatusCode.Should().Be(HttpStatusCode.OK);
        var claims = await user.Content.ReadFromJsonAsync<BffUserClaim[]>();
        claims.Should().Contain(new BffUserClaim("sub", "alice"));
        claims.Should().Contain(new BffUserClaim("name", "alice"), "name falls back to preferred_username");
        claims.Should().Contain(new BffUserClaim("role", "admin"), "Keycloak's realm_access.roles are normalized to role");
        claims.Should().NotContain(c => c.Type == BffDefaults.SessionIdClaim);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        await using var host = await StartAsync();
        var browser = new Browser(host.Client());
        var login = await browser.PostJsonAsync("/bff/login", new { userName = "alice", password = "nope" });
        login.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        browser.Cookie.Should().BeNull();
    }

    [Fact]
    public async Task Calls_without_the_csrf_header_are_rejected()
    {
        await using var host = await StartAsync();
        var browser = await LoginAsync(host);

        (await browser.GetAsync("/bff/user", withCsrf: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.GetAsync("/api/catalog/items", withCsrf: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_event_stream_route_lets_EventSource_reads_through_without_the_csrf_header()
    {
        var settings = WebSettings();
        settings["Bff:Clients:web:RemoteApis:1:LocalPath"] = "/realtime";
        settings["Bff:Clients:web:RemoteApis:1:EventStream"] = "true";
        await using var host = await BffTestHost.StartAsync(settings, bff => bff.AddWebClient());
        var browser = await LoginAsync(host);

        var stream = await browser.GetAsync("/realtime/events", withCsrf: false, accept: "text/event-stream");
        stream.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stream.Content.ReadFromJsonAsync<EchoResponse>())!.Authorization.Should().Be("Bearer at-1", "the session's token is relayed");

        (await browser.GetAsync("/realtime/events", withCsrf: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "only an event-stream read is exempt");
        (await browser.SendAsync(HttpMethod.Post, "/realtime/events", withCsrf: false, accept: "text/event-stream")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "an unsafe method never is");
        (await browser.GetAsync("/api/catalog/items", withCsrf: false, accept: "text/event-stream")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "only routes marked EventStream");
        (await new Browser(host.Client()).GetAsync("/realtime/events", withCsrf: false, accept: "text/event-stream")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a session is still required");
    }

    [Fact]
    public async Task Anonymous_calls_to_protected_routes_get_401_not_a_redirect()
    {
        await using var host = await StartAsync();
        var browser = new Browser(host.Client());
        (await browser.GetAsync("/bff/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.GetAsync("/api/catalog/items")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("ServerSide")]
    [InlineData("Cookie")]
    public async Task Proxy_relays_the_server_side_token_and_strips_the_cookie(string storage)
    {
        await using var host = await StartAsync(storage);
        var browser = await LoginAsync(host);

        var response = await browser.GetAsync("/api/catalog/items?page=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<EchoResponse>();
        echo!.Path.Should().Be("/v1/catalog/items?page=2");
        echo.Authorization.Should().Be("Bearer at-1");
        echo.ClientApp.Should().Be("web");
        echo.Cookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_is_refreshed_before_it_expires()
    {
        await using var host = await StartAsync();
        host.Auth.ExpiresIn = 30; // inside the default one-minute refresh window
        var browser = await LoginAsync(host);
        host.Auth.ExpiresIn = 3600;

        var echo = await (await browser.GetAsync("/api/catalog/items")).Content.ReadFromJsonAsync<EchoResponse>();
        echo!.Authorization.Should().Be("Bearer at-2");
        host.Auth.RefreshCount.Should().Be(1);

        echo = await (await browser.GetAsync("/api/catalog/items")).Content.ReadFromJsonAsync<EchoResponse>();
        echo!.Authorization.Should().Be("Bearer at-2");
        host.Auth.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_refresh()
    {
        await using var host = await StartAsync();
        host.Auth.ExpiresIn = 30;
        var browser = await LoginAsync(host);
        host.Auth.ExpiresIn = 3600;

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => browser.GetAsync("/api/catalog/items")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        host.Auth.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session()
    {
        await using var host = await StartAsync();
        host.Auth.ExpiresIn = 30;
        var browser = await LoginAsync(host);
        host.Auth.RefuseRefresh = true;

        var proxied = await browser.GetAsync("/api/catalog/items");
        proxied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await proxied.Content.ReadFromJsonAsync<EchoResponse>())!.Authorization.Should().BeEmpty("no token is relayed once the session ended");

        (await browser.GetAsync("/bff/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_tokens_and_ends_the_session()
    {
        await using var host = await StartAsync();
        var browser = await LoginAsync(host);

        var logout = await browser.SendAsync(HttpMethod.Post, "/bff/logout");
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent, "password mode has no end-session redirect");
        host.Auth.Revoked.Should().Contain(["rt-1", "at-1"]);
        (await browser.GetAsync("/bff/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_skips_revocation_when_the_server_has_no_endpoint()
    {
        await using var host = await StartAsync(withRevocation: false); // e.g. Entra ID
        var browser = await LoginAsync(host);

        (await browser.SendAsync(HttpMethod.Post, "/bff/logout")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Auth.Revoked.Should().BeEmpty();
    }

    [Fact]
    public async Task Evicted_server_side_tokens_end_the_session()
    {
        await using var host = await StartAsync();
        var browser = await LoginAsync(host);

        // A logout on another replica (or a cache flush) removes the tokens.
        var cache = host.App.Services.GetRequiredService<ICacheService>();
        var user = await browser.GetAsync("/bff/user");
        user.StatusCode.Should().Be(HttpStatusCode.OK);
        var sid = await GetSessionIdAsync(host, browser);
        await cache.RemoveAsync($"bff:tokens:web:{sid}");

        (await browser.GetAsync("/bff/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> GetSessionIdAsync(BffTestHost host, Browser browser)
    {
        var ticketFormat = host.App.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(BffDefaults.Scheme("web")).TicketDataFormat;
        var value = Uri.UnescapeDataString(browser.Cookie!.Split('=', 2)[1]);
        var ticket = ticketFormat.Unprotect(value);
        await Task.CompletedTask;
        return ticket!.Principal.FindFirst(BffDefaults.SessionIdClaim)!.Value;
    }
}
