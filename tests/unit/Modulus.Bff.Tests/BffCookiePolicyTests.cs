namespace Modulus.Bff.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Modulus.Bff;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>S8: a web client's session cookie is sent only over HTTPS outside Development.</summary>
[Trait("Category", "Unit")]
public sealed class BffCookiePolicyTests
{
    [Fact]
    public async Task The_session_cookie_requires_https_unless_the_host_is_in_development()
    {
        await using var host = await BffTestHost.StartAsync(new()
        {
            ["Bff:Clients:web:PathPrefix"] = "/web",
            ["Bff:Clients:web:LoginMode"] = "Password",
            ["Bff:Clients:web:ClientId"] = "shop-web",
            ["Bff:Clients:web:RemoteApis:0:LocalPath"] = "/api",
        }, bff => bff.AddWebClient());

        var cookie = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(BffDefaults.Scheme("web"));

        // The test host's environment is whatever the runner set; the rule is what must hold for it.
        var expected = host.App.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        cookie.Cookie.SecurePolicy.Should().Be(expected);
        cookie.Cookie.HttpOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Password_sign_in_is_rate_limited_per_address_without_any_client_configuration()
    {
        await using var host = await BffTestHost.StartAsync(new()
        {
            ["Bff:Clients:web:PathPrefix"] = "/web",
            ["Bff:Clients:web:LoginMode"] = "Password",
            ["Bff:Clients:web:ClientId"] = "shop-web",
            ["Bff:Clients:web:RemoteApis:0:LocalPath"] = "/api",
        }, bff => bff.AddWebClient());

        var browser = new Browser(host.Client());
        var statuses = new List<System.Net.HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await browser.PostJsonAsync("/web/bff/login", new { userName = "alice", password = "wrong" })).StatusCode);

        statuses.Take(10).Should().AllSatisfy(s => s.Should().NotBe(System.Net.HttpStatusCode.TooManyRequests));
        statuses.Skip(10).Should().AllSatisfy(s => s.Should().Be(System.Net.HttpStatusCode.TooManyRequests));
    }
}
