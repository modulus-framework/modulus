using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.AspNetCore.Endpoints;
using Modulus.AspNetCore.HealthChecks;
using Modulus.AspNetCore.Security.Policy;
using Modulus.Core.Abstractions.Security;
using Xunit;

namespace Modulus.AspNetCore.Tests;

/// <summary>
/// The startup security guard: every endpoint resolves to a policy, every anonymous endpoint carries a
/// reason and (outside Development) is on the loosening allow-list.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityGuardTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"modulus-guard-{Guid.NewGuid():N}");

    public SecurityGuardTests() => Directory.CreateDirectory(_contentRoot);

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    private async Task<WebApplication> StartAsync(
        Action<WebApplication> map,
        string environment = "Production",
        bool fallback = false,
        string? allowList = null)
    {
        if (allowList is not null)
        {
            Directory.CreateDirectory(Path.Combine(_contentRoot, "security"));
            await File.WriteAllTextAsync(Path.Combine(_contentRoot, "security", "loosening-allowlist.json"), allowList);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = _contentRoot,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoUserHandler>("Test", null);
        builder.Services.AddAuthorization(o =>
        {
            if (fallback)
                o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        builder.Services.AddModulusSecurityGuard(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    [Fact]
    public async Task An_endpoint_without_any_policy_fails_startup()
    {
        var act = () => StartAsync(app => app.MapGet("/raw", () => "open"));

        (await act.Should().ThrowAsync<SecurityGuardException>())
            .Which.Findings.Should().ContainSingle(f => f.Code == "unpoliced" && f.Route == "GET /raw");
    }

    [Fact]
    public async Task With_a_fallback_policy_a_raw_endpoint_is_closed_and_starts()
    {
        await using var app = await StartAsync(a => a.MapGet("/raw", () => "open"), fallback: true);

        var entry = app.Services.GetRequiredService<SecurityGuardState>().Report.Endpoints.Single(e => e.Route == "/raw");
        entry.Access.Should().Be(EndpointAccess.Fallback);
        (await app.GetTestClient().GetAsync("/raw")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_bare_AllowAnonymous_fails_startup_in_every_environment(string environment)
    {
        var act = () => StartAsync(a => a.MapGet("/open", () => "x").AllowAnonymous(), environment);

        (await act.Should().ThrowAsync<SecurityGuardException>())
            .Which.Findings.Should().ContainSingle(f => f.Code == "anonymous-without-reason");
    }

    [Fact]
    public async Task A_loosening_missing_from_the_allow_list_fails_outside_Development()
    {
        var act = () => StartAsync(a => a.MapGet("/public", () => "x").Loosen("Public price list"));

        (await act.Should().ThrowAsync<SecurityGuardException>())
            .Which.Findings.Should().ContainSingle(f => f.Code == "loosening-not-allow-listed");
    }

    [Fact]
    public async Task A_loosening_missing_from_the_allow_list_only_warns_in_Development()
    {
        await using var app = await StartAsync(a => a.MapGet("/public", () => "x").Loosen("Public price list"), "Development");

        var report = app.Services.GetRequiredService<SecurityGuardState>().Report;
        report.Findings.Should().ContainSingle(f => f.Severity == SecurityGuardSeverity.Unlisted);
        (await app.GetTestClient().GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_allow_listed_loosening_starts_and_is_reported_with_its_reason()
    {
        const string list = """{ "endpoints": [ { "route": "/public", "methods": ["GET"], "reason": "listed" } ] }""";
        await using var app = await StartAsync(
            a => a.MapGet("/public", () => "x").Loosen("Public price list", ticket: "SEC-12"), allowList: list);

        var entry = app.Services.GetRequiredService<SecurityGuardState>().Report.Loosened.Single();
        entry.LooseningReason.Should().Be("Public price list", "the endpoint's own reason wins");
        entry.Ticket.Should().Be("SEC-12");
        entry.AllowListed.Should().BeTrue();
        (await app.GetTestClient().GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Framework_loosenings_are_reported_but_need_no_allow_list_entry()
    {
        await using var app = await StartAsync(a => a.MapModulusHealthChecks());

        var loosened = app.Services.GetRequiredService<SecurityGuardState>().Report.Loosened.ToList();
        loosened.Select(e => e.Route).Should().BeEquivalentTo(["/health/live", "/health/ready"]);
        loosened.Should().OnlyContain(e => !e.AllowListed && e.LooseningReason != null);
        (await app.GetTestClient().GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_allow_list_reason_explains_a_third_party_bare_AllowAnonymous()
    {
        const string list = """{ "endpoints": [ { "route": "/vendor/*", "reason": "Vendor callback, HMAC-verified" } ] }""";
        await using var app = await StartAsync(a => a.MapPost("/vendor/hook", () => "x").AllowAnonymous(), allowList: list);

        app.Services.GetRequiredService<SecurityGuardState>().Report.Loosened.Single()
            .LooseningReason.Should().Be("Vendor callback, HMAC-verified");
    }

    [Fact]
    public async Task Classified_data_can_never_be_anonymous()
    {
        const string list = """{ "endpoints": [ { "route": "/payroll" } ] }""";
        var act = () => StartAsync(
            a => a.MapGet("/payroll", () => "x").Loosen("oops").WithSecurityPolicy(DataClassification.Restricted),
            allowList: list);

        (await act.Should().ThrowAsync<SecurityGuardException>())
            .Which.Findings.Should().ContainSingle(f => f.Code == "anonymous-classified-data");
    }

    [Fact]
    public async Task A_network_requirement_fails_until_it_can_be_enforced()
    {
        var act = () => StartAsync(
            a => a.MapGet("/branch", () => "x").RequireAuthorization()
                .WithSecurityPolicy(DataClassification.Internal, NetworkRequirement.BranchNetwork));

        (await act.Should().ThrowAsync<SecurityGuardException>())
            .Which.Findings.Should().ContainSingle(f => f.Code == "network-not-enforced");
    }

    [Fact]
    public async Task Policed_endpoints_report_their_policies()
    {
        await using var app = await StartAsync(a =>
        {
            a.MapGet("/admin", () => "x").RequireAuthorization("catalog:products:manage");
            a.MapGet("/me", () => "x").RequireAuthorization();
        });

        var endpoints = app.Services.GetRequiredService<SecurityGuardState>().Report.Endpoints;
        endpoints.Single(e => e.Route == "/admin").Policies.Should().Equal("catalog:products:manage");
        endpoints.Single(e => e.Route == "/me").Policies.Should().Equal("(authenticated)");
    }

    [Fact]
    public async Task A_disabled_guard_does_nothing()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddModulusSecurityGuard(builder.Configuration, o => o.Enabled = false);
        await using var app = builder.Build();
        app.MapGet("/raw", () => "open");

        await app.StartAsync();

        app.Services.GetRequiredService<SecurityGuardState>().Report.Should().BeSameAs(SecurityGuardReport.Empty);
    }

    [Fact]
    public void REPR_AllowAnonymous_with_a_reason_carries_the_loosening()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        using var app = builder.Build();
        app.MapModulusEndpoints(typeof(PublicCatalogEndpoint).Assembly);

        var report = EndpointSecurityAnalyzer.Analyze(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints), hasFallbackPolicy: false, LooseningAllowList.Empty);

        var entry = report.Endpoints.Single(e => e.Route == "/api/v1/public-catalog");
        entry.Access.Should().Be(EndpointAccess.Loosened);
        entry.LooseningReason.Should().Be("Public catalog for the storefront");
        entry.Ticket.Should().Be("SEC-7");
        entry.Classification.Should().Be(DataClassification.Public);
    }

    [Theory]
    [InlineData("/connect/*", "/connect/token", true)]
    [InlineData("/connect/*", "/connect", true)]
    [InlineData("/connect/*", "/connectx", false)]
    [InlineData("/Account/*", "/account/login", true)]
    [InlineData("/health/live", "/health/live/", true)]
    [InlineData("/health/live", "/health/ready", false)]
    public void Allow_list_routes_match_exactly_or_by_prefix(string pattern, string route, bool covered)
    {
        var list = new LooseningAllowList { Endpoints = [new LooseningAllowListEntry { Route = pattern }] };

        (list.Find(route, ["GET"]) is not null).Should().Be(covered);
    }

    [Fact]
    public void Allow_list_methods_must_cover_every_method_of_the_endpoint()
    {
        var list = new LooseningAllowList
        {
            Endpoints = [new LooseningAllowListEntry { Route = "/x", Methods = ["GET"] }],
        };

        list.Find("/x", ["GET"]).Should().NotBeNull();
        list.Find("/x", ["GET", "POST"]).Should().BeNull();
        list.Find("/x", []).Should().BeNull("an endpoint without methods answers every method");
    }

    [Fact]
    public void An_invalid_allow_list_file_is_reported()
    {
        var path = Path.Combine(_contentRoot, "bad.json");
        File.WriteAllText(path, "{ not json");

        var act = () => LooseningAllowList.Load(path);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not valid JSON*");
    }

    private sealed class NoUserHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}

public sealed class PublicCatalogEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/api/v1/public-catalog");
        AllowAnonymous("Public catalog for the storefront", ticket: "SEC-7");
        Classification(DataClassification.Public);
    }

    protected override Task HandleAsync(CancellationToken ct) => SendOkAsync("catalog", ct);
}
