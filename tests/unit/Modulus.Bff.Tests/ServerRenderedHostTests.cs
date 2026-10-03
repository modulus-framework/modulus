namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Bff.Tokens;
using Xunit;

/// <summary>
/// A server-rendered host (Razor Pages, MVC) that is one web client: its pages use the client's session through
/// <see cref="BffBuilder.SetDefaultClient"/>, sign users in and out with <see cref="IBffSessionService"/> and call the
/// API through typed clients that carry the session's token (<see cref="BffHttpClientExtensions.AddBffUserAccessToken"/>).
/// Plain endpoints stand in for pages: neither carries BFF endpoint metadata.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ServerRenderedHostTests
{
    private static Task<BffTestHost> StartAsync() => BffTestHost.StartAsync(
        new()
        {
            ["Bff:AuthServer"] = "Keycloak",
            ["Bff:Clients:web:ClientId"] = "shop",
            ["Bff:Clients:web:LoginMode"] = "Password",
            ["Bff:Clients:web:LoginPath"] = "/Account/Login",
            ["Bff:Clients:web:AccessDeniedPath"] = "/Account/AccessDenied",
        },
        bff =>
        {
            bff.AddWebClient().SetDefaultClient("web");
            bff.Services.AddHttpClient<EchoApi>(http => http.BaseAddress = new Uri("https://api/")).AddBffUserAccessToken();
        },
        app =>
        {
            app.MapGet("/page", async (EchoApi api, HttpContext context) => Results.Ok(new
            {
                user = context.User.Identity?.Name,
                admin = context.User.IsInRole("admin"),
                echo = await api.GetAsync(context.RequestAborted),
            })).RequireAuthorization();
            app.MapGet("/admin-page", () => Results.Ok()).RequireAuthorization(p => p.RequireRole("superuser"));
            app.MapPost("/Account/Login", async (IBffSessionService session, HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var result = await session.SignInWithPasswordAsync(context, "web", form["user"].ToString(), form["password"].ToString());
                return result.Succeeded ? Results.Redirect("/page") : Results.BadRequest(new { result.Error });
            });
            app.MapPost("/Account/Logout", async (IBffSessionService session, HttpContext context) =>
                Results.Ok(new { endSessionUrl = await session.SignOutAsync(context, "web", "/") }));
        });

    private static async Task<Browser> SignInAsync(BffTestHost host)
    {
        var browser = new Browser(host.Client());
        var login = await browser.SendAsync(HttpMethod.Post, "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["user"] = "alice", ["password"] = "pw" }), withCsrf: false);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    [Fact]
    public async Task Anonymous_page_request_is_sent_to_the_sign_in_page()
    {
        await using var host = await StartAsync();

        var response = await new Browser(host.Client()).GetAsync("/page", withCsrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login").And.Contain("ReturnUrl=%2Fpage");
    }

    [Fact]
    public async Task Bff_endpoints_still_answer_401()
    {
        await using var host = await StartAsync();
        (await new Browser(host.Client()).GetAsync("/bff/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_page_calls_the_api_with_the_session_token()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);
        browser.Cookie.Should().NotContain(host.Auth.CurrentAccessToken!, "the token stays on the server");

        var page = await browser.GetAsync("/page", withCsrf: false);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await page.Content.ReadFromJsonAsync<PageResponse>();
        body!.User.Should().Be("alice");
        body.Admin.Should().BeTrue("roles are normalized so IsInRole works on pages");
        body.Echo.Authorization.Should().Be("Bearer " + host.Auth.CurrentAccessToken);
        body.Echo.ClientApp.Should().Be("web");
    }

    [Fact]
    public async Task Forbidden_page_goes_to_the_access_denied_page()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);

        var response = await browser.GetAsync("/admin-page", withCsrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/AccessDenied");
    }

    [Fact]
    public async Task Sign_out_revokes_the_tokens_and_ends_the_session()
    {
        await using var host = await StartAsync();
        var browser = await SignInAsync(host);

        var logout = await browser.SendAsync(HttpMethod.Post, "/Account/Logout", withCsrf: false);

        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        (await logout.Content.ReadFromJsonAsync<LogoutResponse>())!.EndSessionUrl.Should().BeNull("password mode has no RP-initiated logout");
        host.Auth.Revoked.Should().NotBeEmpty();
        (await browser.GetAsync("/page", withCsrf: false)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Default_client_must_be_registered()
    {
        var act = () => BffTestHost.StartAsync([], bff => bff.AddMobileClient().SetDefaultClient("web"));
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("'web' is not registered");
    }

    private sealed record PageResponse(string? User, bool Admin, EchoResponse Echo);

    private sealed record LogoutResponse(string? EndSessionUrl);
}

internal sealed class EchoApi(HttpClient http)
{
    public async Task<EchoResponse> GetAsync(CancellationToken ct)
        => (await http.GetFromJsonAsync<EchoResponse>("items", ct))!;
}
