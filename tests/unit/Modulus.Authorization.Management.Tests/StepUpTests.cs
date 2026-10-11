using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>Step-up authentication: sensitive permissions need a recent multi-factor sign-in, answered with RFC 9470.</summary>
[Trait("Category", "Unit")]
public sealed class StepUpTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => s_now;
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-User"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            void Many(string header, string type)
                => claims.AddRange(Request.Headers[header].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => new Claim(type, v)));
            Many("X-Test-Permissions", "permission");
            Many("X-Test-Amr", "amr");
            Many("X-Test-Roles", "role");
            if (Request.Headers.ContainsKey("X-Test-Machine"))
                claims.Add(new Claim(StepUpPolicy.MachineClaim, "true"));
            if (Request.Headers.TryGetValue("X-Test-AuthTime", out var authTime))
                claims.Add(new Claim("auth_time", authTime.ToString(), ClaimValueTypes.Integer64));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private static async Task<WebApplication> StartAsync(Action<StepUpOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddSingleton<TimeProvider>(new FixedClock());
        builder.Services.AddAuthorization();
        builder.Services.AddModulusAuthorization(o =>
        {
            o.TrustPermissionClaims = true;
            configure?.Invoke(o.StepUp);
        });
        builder.Services.AddPermissions("Demo", registry =>
        {
            registry.Add("demo:pay", "Release a payment.", null, PermissionSensitivity.Critical);
            registry.Add("demo:view", "View payments.");
            registry.Add("demo:export", "Export payments.");
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/pay", () => "ok").RequireAuthorization("demo:pay");
        app.MapGet("/view", () => "ok").RequireAuthorization("demo:view");
        app.MapGet("/export", () => "ok").RequireAuthorization("demo:export");
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app, string permissions, string? amr = null, DateTimeOffset? authTime = null, string? roles = null, bool machine = false)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "u");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (amr is not null)
            client.DefaultRequestHeaders.Add("X-Test-Amr", amr);
        if (authTime is { } t)
            client.DefaultRequestHeaders.Add("X-Test-AuthTime", t.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (roles is not null)
            client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
        if (machine)
            client.DefaultRequestHeaders.Add("X-Test-Machine", "1");
        return client;
    }

    [Fact]
    public async Task Without_the_option_a_critical_permission_needs_nothing_extra()
    {
        await using var app = await StartAsync(null);
        using var client = Client(app, "demo:pay");
        (await client.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_critical_permission_needs_a_fresh_multi_factor_sign_in_and_the_answer_says_so()
    {
        await using var app = await StartAsync(o => o.RequireForCritical = true);

        using var password = Client(app, "demo:pay", "pwd", s_now.AddMinutes(-1));
        var refused = await password.GetAsync("/pay");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = refused.Headers.WwwAuthenticate.ToString();
        challenge.Should().Contain("insufficient_user_authentication").And.Contain("acr_values=\"mfa\"").And.Contain("max_age=\"900\"");
        (await refused.Content.ReadAsStringAsync()).Should().Contain("STEP_UP_REQUIRED");

        using var fresh = Client(app, "demo:pay", "pwd,otp,mfa", s_now.AddMinutes(-5));
        (await fresh.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var stale = Client(app, "demo:pay", "pwd,otp,mfa", s_now.AddMinutes(-16));
        (await stale.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var noTime = Client(app, "demo:pay", "mfa");
        (await noTime.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var future = Client(app, "demo:pay", "mfa", s_now.AddHours(2));
        (await future.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Not_holding_the_permission_is_a_plain_403_and_other_permissions_are_unaffected()
    {
        await using var app = await StartAsync(o => o.RequireForCritical = true);

        using var without = Client(app, "demo:view", "pwd", s_now);
        (await without.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await without.GetAsync("/view")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Named_permissions_need_it_too_and_machine_principals_are_exempt()
    {
        await using var app = await StartAsync(o =>
        {
            o.RequireForCritical = true;
            o.Permissions.Add("demo:export");
            o.MaxAge = TimeSpan.FromMinutes(5);
        });

        using var password = Client(app, "demo:export,demo:pay", "pwd", s_now);
        (await password.GetAsync("/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var mfa = Client(app, "demo:export", "mfa", s_now.AddMinutes(-4));
        (await mfa.GetAsync("/export")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var tooOld = Client(app, "demo:export", "mfa", s_now.AddMinutes(-6));
        (await tooOld.GetAsync("/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A person holding an integration role (assigned by an admin or an identity provider) is not a machine: still refused.
        using var impostor = Client(app, "demo:pay", roles: "integration:erp-sync");
        (await impostor.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // An integration client has no second factor to give; its grants limit it instead. Only the token server's
        // client-credentials token carries the machine marker.
        using var machine = Client(app, "demo:pay", roles: "integration:erp-sync", machine: true);
        (await machine.GetAsync("/pay")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
