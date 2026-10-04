namespace Modulus.Testing.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using Modulus.Testing.Security;
using Xunit;

/// <summary>Phase 5 of the security plan: the generated probes catch an endpoint that forgot its policy.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityProbeSuiteTests
{
    private static readonly Guid Member = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid Foreign = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static async Task<WebApplication> HostAsync(bool withLeak)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthDefaults.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthDefaults.SchemeName, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("orders:read", p => p.RequireClaim("permission", "orders:read"));
            o.AddPolicy("orders:write", p => p.RequireClaim("permission", "orders:write"));
        });

        var app = builder.Build();
        app.UseAuthentication();

        // Stands in for TenantMiddleware with RequireMembership(): a selected company needs a membership (a pinned
        // tid claim here), whatever the caller's permissions.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var selected)
                && context.User.Identity?.IsAuthenticated == true
                && context.User.FindFirst("tid")?.Value != selected.ToString()
                && selected.ToString() != Member.ToString())
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
        app.UseAuthorization();

        app.MapGet("/health", () => "ok").AllowAnonymous();
        app.MapGet("/me", () => "me").RequireAuthorization();
        app.MapGet("/orders/{id:guid}", (Guid id) => id).RequireAuthorization("orders:read");
        app.MapPost("/orders", () => Results.Created("/orders/1", null)).RequireAuthorization("orders:write");
        app.MapGet("/items/{page:int}/{**rest}", (int page) => page).RequireAuthorization("orders:read");
        if (withLeak)
            app.MapDelete("/orders/{id:guid}", (Guid id) => Results.NoContent());

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task A_policed_host_passes_every_probe()
    {
        await using var app = await HostAsync(withLeak: false);

        var report = await SecurityProbeSuite.RunAsync(app.Services, app.GetTestClient(), new SecurityProbeOptions { ForeignTenantId = Foreign });

        report.Failures.Should().BeEmpty();
        report.Loosened.Should().Equal("GET /health");
        report.Results.Where(r => r.Probe == SecurityProbe.Anonymous).Select(r => r.Route)
            .Should().BeEquivalentTo(["/me", "/orders/{id:guid}", "/orders", "/items/{page:int}/{**rest}"]);
        report.Results.Where(r => r.Probe == SecurityProbe.NoPermission).Should().HaveCount(3, "/me only needs a sign-in");
        report.Results.Where(r => r.Probe == SecurityProbe.ForeignTenant).Should().HaveCount(4);
        report.Results.Single(r => r.Route == "/orders/{id:guid}" && r.Probe == SecurityProbe.Anonymous).Url
            .Should().Be("/orders/00000000-0000-0000-0000-00000000c0de");
        report.Results.Single(r => r.Route.StartsWith("/items", StringComparison.Ordinal) && r.Probe == SecurityProbe.Anonymous).Url
            .Should().StartWith("/items/1/");
        report.EnsureNoFailures();
    }

    [Fact]
    public async Task The_token_servers_userinfo_endpoint_may_refuse_a_foreign_company_with_400()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthDefaults.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthDefaults.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();

        // Like OpenIddict: the token server answers userinfo during authentication, before the tenant is resolved.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/connect/userinfo")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context);
        });
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Tenant-Id"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
        app.UseAuthorization();
        app.MapGet("/connect/userinfo", () => "claims").RequireAuthorization();
        await app.StartAsync();

        var report = await SecurityProbeSuite.RunAsync(app.Services, app.GetTestClient(), new SecurityProbeOptions { ForeignTenantId = Foreign });

        report.Results.Single(r => r.Probe == SecurityProbe.ForeignTenant).Expected.Should().Be("400/403");
        report.EnsureNoFailures();

        var strict = new SecurityProbeOptions { ForeignTenantId = Foreign };
        strict.ForeignTenantStatusOverrides.Clear();
        (await SecurityProbeSuite.RunAsync(app.Services, app.GetTestClient(), strict)).Failures
            .Should().ContainSingle(r => r.Probe == SecurityProbe.ForeignTenant && r.Actual == 400);
    }

    [Fact]
    public async Task An_endpoint_behind_another_scheme_answers_the_signed_in_probes_with_401()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthDefaults.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthDefaults.SchemeName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, NeverAuthenticates>("ApiKey", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("service", p => p.AddAuthenticationSchemes("ApiKey").RequireAuthenticatedUser()));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/_ai/connector/manifest", () => "manifest").RequireAuthorization("service");
        await app.StartAsync();

        var report = await SecurityProbeSuite.RunAsync(app.Services, app.GetTestClient(), new SecurityProbeOptions { ForeignTenantId = Foreign });

        report.Results.Select(r => (r.Probe, r.Expected)).Should().BeEquivalentTo(new[]
        {
            (SecurityProbe.Anonymous, "401"),
            (SecurityProbe.NoPermission, "401"),
            (SecurityProbe.ForeignTenant, "401/403"),
        });
        report.EnsureNoFailures();
    }

    private sealed class NeverAuthenticates(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    [Fact]
    public async Task An_endpoint_without_a_policy_fails_the_suite()
    {
        await using var app = await HostAsync(withLeak: true);

        var report = await SecurityProbeSuite.RunAsync(app.Services, app.GetTestClient());

        var failure = report.Failures.Should().ContainSingle().Subject;
        failure.Method.Should().Be("DELETE");
        failure.Probe.Should().Be(SecurityProbe.Anonymous);
        failure.Actual.Should().Be(204);
        var act = report.EnsureNoFailures;
        act.Should().Throw<SecurityProbeException>().WithMessage("*DELETE /orders/*expected 401, got 204*");
    }

    [Fact]
    public async Task The_isolation_helper_passes_for_a_tenant_entity_and_catches_a_shared_one()
    {
        var connectionString = $"Data Source=probe-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, AmbientTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<LedgerDbContext>(o => o.UseSqlite(connectionString));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.EnsureCreatedAsync();

        await provider.AssertTenantIsolationAsync<LedgerDbContext, Invoice>(new Invoice { Id = Guid.NewGuid() }, Member, Foreign);

        // A shared entity whose tenant id is ignored by the model (no filter): the helper must report it.
        var leak = () => provider.AssertTenantIsolationAsync<LedgerDbContext, LeakyInvoice>(new LeakyInvoice { Id = Guid.NewGuid() }, Member, Foreign);
        await leak.Should().ThrowAsync<TenantIsolationException>().WithMessage("*found by key*");
    }

    private sealed class AmbientTenant : ICurrentTenant
    {
        private static readonly AsyncLocal<(Guid? Id, bool Host)> s_current = new();

        public Guid? TenantId => s_current.Value.Id;

        public string? TenantSlug => TenantId?.ToString();

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => s_current.Value.Host;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = s_current.Value;
            s_current.Value = tenant is null ? (null, true) : (tenant.TenantId, false);
            return new Restore(() => s_current.Value = previous);
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }

    private sealed class LedgerDbContext(
        DbContextOptions<LedgerDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "ledger_";

        public DbSet<Invoice> Invoices => Set<Invoice>();

        public DbSet<LeakyInvoice> LeakyInvoices => Set<LeakyInvoice>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<LeakyInvoice>().HasQueryFilter(null);
        }
    }

    private sealed class Invoice : IHasTenantId
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }

    private sealed class LeakyInvoice : IHasTenantId
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }
}
