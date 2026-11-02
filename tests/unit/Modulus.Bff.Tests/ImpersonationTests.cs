namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Bff.Tokens;
using Xunit;

/// <summary>
/// Impersonation in a web session: the cookie switches to the impersonated person's tokens and claims, the caller's own session is parked
/// (its tokens stay in the store), and ending the impersonation revokes the impersonated tokens and restores the caller's session.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ImpersonationTests
{
    private static Task<BffTestHost> StartAsync(string tokenStorage = "ServerSide") => BffTestHost.StartAsync(
        new()
        {
            ["Bff:AuthServer"] = "Keycloak",
            ["Bff:Clients:web:ClientId"] = "shop",
            ["Bff:Clients:web:LoginMode"] = "Password",
            ["Bff:Clients:web:TokenStorage"] = tokenStorage,
        },
        bff =>
        {
            bff.AddWebClient().SetDefaultClient("web");
            bff.Services.AddHttpClient<EchoApi>(http => http.BaseAddress = new Uri("https://api/")).AddBffUserAccessToken();
        },
        app =>
        {
            app.MapPost("/Account/Login", async (IBffSessionService session, HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var result = await session.SignInWithPasswordAsync(context, "web", form["user"].ToString(), form["password"].ToString());
                return result.Succeeded ? Results.Redirect("/page") : Results.BadRequest(new { result.Error });
            });
            app.MapPost("/Admin/Impersonate", async (IBffSessionService session, HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var result = await session.StartImpersonationAsync(context, "web", form["code"].ToString(), context.RequestAborted);
                return result.Succeeded ? Results.Ok() : Results.BadRequest(new { result.Error });
            });
            app.MapGet("/page", async (EchoApi api, HttpContext context) => Results.Ok(new
            {
                echo = await api.GetAsync(context.RequestAborted),
            })).RequireAuthorization();
        });

    private static async Task<Browser> SignInAsync(BffTestHost host)
    {
        var browser = new Browser(host.Client());
        var login = await browser.SendAsync(HttpMethod.Post, "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["user"] = "alice", ["password"] = "pw" }), withCsrf: false);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static async Task<string?> CurrentBearerAsync(Browser browser)
    {
        var page = await browser.GetAsync("/page", withCsrf: false);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await page.Content.ReadFromJsonAsync<PageResponse>())!.Echo.Authorization;
    }

    [Fact]
    public async Task Starting_switches_the_session_and_ending_restores_the_callers_session()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);
        var callersToken = await CurrentBearerAsync(browser);

        var start = await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "imp-1" }), withCsrf: false);

        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var impersonated = await CurrentBearerAsync(browser);
        impersonated.Should().NotBe(callersToken, "the impersonated person's own token is used from now on");

        var claims = await (await browser.GetAsync("/bff/user")).Content.ReadFromJsonAsync<List<BffUserClaim>>();
        claims!.Should().Contain(c => c.Type == BffDefaults.ImpersonatorClaim && c.Value == "alice");
        claims.Should().NotContain(c => c.Type == BffDefaults.ActorSessionClaim, "the parked session id stays on the server");

        var end = await browser.SendAsync(HttpMethod.Post, "/bff/impersonation/exit");

        end.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CurrentBearerAsync(browser)).Should().Be(callersToken, "the caller's own session comes back");
        host.Auth.Revoked.Should().Contain(impersonated!.Replace("Bearer ", string.Empty), "the impersonated token is revoked when it ends");
    }

    [Fact]
    public async Task A_second_impersonation_while_impersonating_is_refused()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);
        await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "imp-1" }), withCsrf: false);

        var again = await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "imp-1" }), withCsrf: false);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await again.Content.ReadAsStringAsync()).Should().Contain("already_impersonating");
    }

    [Fact]
    public async Task Ending_without_impersonating_is_a_conflict()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);

        var end = await browser.SendAsync(HttpMethod.Post, "/bff/impersonation/exit");

        end.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_bad_code_is_refused_and_the_caller_keeps_the_session()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);
        var callersToken = await CurrentBearerAsync(browser);

        var start = await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "stale" }), withCsrf: false);

        start.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CurrentBearerAsync(browser)).Should().Be(callersToken);
    }

    [Fact]
    public async Task Impersonation_needs_server_side_token_storage()
    {
        await using var host = await StartAsync("Cookie");
        var browser = await SignInAsync(host);

        var start = await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "imp-1" }), withCsrf: false);

        start.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await start.Content.ReadAsStringAsync()).Should().Contain("impersonation_unsupported");
    }

    [Fact]
    public async Task Signing_out_while_impersonating_also_revokes_the_callers_parked_tokens()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);
        var callersToken = (await CurrentBearerAsync(browser))!.Replace("Bearer ", string.Empty);
        await browser.SendAsync(HttpMethod.Post, "/Admin/Impersonate", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "imp-1" }), withCsrf: false);

        var logout = await browser.SendAsync(HttpMethod.Post, "/bff/logout");

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Auth.Revoked.Should().Contain(callersToken, "the parked session ends with the browser's sign-out");
    }

    private sealed record PageResponse(EchoResponse Echo);
}
