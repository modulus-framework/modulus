using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.EntityFrameworkCore.Audit;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

// Verifies that mutating management-API calls durably record an administrative
// audit event (auth blueprint §5.14/§16) — a representative slice (grant,
// org-unit, delegation) rather than all twelve mutating endpoints, since every
// one of them goes through the same EmitAuditAsync helper.
[Trait("Category", "Unit")]
public sealed class AuthorizationManagementAuditTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddModulusAuthorization();
        builder.Services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        builder.Services.AddEfCoreAuthorizationAudit();
        builder.Services.AddModulusAuthorizationManagement();

        // The management package's own TryAdd only guarantees SOME ICurrentUser
        // is resolvable (NullCurrentUser) — bridging HttpContext.User claims to
        // it is normally Modulus.Identity's job. A minimal test double stands
        // in here so ActorUserId assertions below exercise the real capture
        // path rather than always observing null.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, TestCurrentUser>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusAuthorizationManagement();

        using (var db = _app.Services
                   .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
                   .CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-Authenticated", "yes");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    private sealed class TestCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public Guid? UserId =>
            Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
                ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
        public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new("permission", AuthorizationManagementExtensions.ManagePermission),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private async Task<List<OutboxMessage>> ReadAuditRowsAsync()
    {
        await using var db = await _app.Services
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContextAsync();
        return await db.Set<OutboxMessage>().ToListAsync();
    }

    [Fact]
    public async Task Grant_creation_is_audited()
    {
        (await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "manager",
            permissions = new[] { "orders:read" },
            type = "Allow",
        })).EnsureSuccessStatusCode();

        var rows = await ReadAuditRowsAsync();
        rows.Should().ContainSingle();
        rows[0].MessageType.Should().Be("authorization.administrative-change.v1");

        using var payload = JsonDocument.Parse(rows[0].Payload);
        payload.RootElement.GetProperty("category").GetString().Should().Be("Grant");
        payload.RootElement.GetProperty("action").GetString().Should().Be("Granted");
        payload.RootElement.GetProperty("targetDescription").GetString().Should().Be("Role:manager");
        payload.RootElement.GetProperty("actorUserId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Grant_revocation_is_audited()
    {
        await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "manager",
            permissions = new[] { "orders:read" },
        });

        await _client.DeleteAsync("/authorization/grants/role/manager/orders:read");

        var rows = await ReadAuditRowsAsync();
        rows.Should().HaveCount(2);
        using var payload = JsonDocument.Parse(rows[1].Payload);
        payload.RootElement.GetProperty("action").GetString().Should().Be("Revoked");
    }

    [Fact]
    public async Task Org_unit_creation_is_audited()
    {
        var id = Guid.NewGuid();
        (await _client.PostAsJsonAsync("/authorization/org/units", new { id }))
            .EnsureSuccessStatusCode();

        var rows = await ReadAuditRowsAsync();
        rows.Should().ContainSingle();
        using var payload = JsonDocument.Parse(rows[0].Payload);
        payload.RootElement.GetProperty("category").GetString().Should().Be("OrgUnit");
        payload.RootElement.GetProperty("action").GetString().Should().Be("Created");
        payload.RootElement.GetProperty("targetDescription").GetString().Should().Be($"unit:{id}");
    }

    [Fact]
    public async Task Delegation_creation_is_audited()
    {
        var now = DateTimeOffset.UtcNow;
        var (from, to) = (Guid.NewGuid(), Guid.NewGuid());

        (await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = from,
            fromRoles = new[] { "manager" },
            toUserId = to,
            permissions = new[] { "orders:approve" },
            notBefore = now,
            notAfter = now.AddDays(1),
        })).EnsureSuccessStatusCode();

        var rows = await ReadAuditRowsAsync();
        rows.Should().ContainSingle();
        using var payload = JsonDocument.Parse(rows[0].Payload);
        payload.RootElement.GetProperty("category").GetString().Should().Be("Delegation");
        payload.RootElement.GetProperty("action").GetString().Should().Be("Created");
        payload.RootElement.GetProperty("targetDescription").GetString()
            .Should().Be($"from:{from} -> to:{to}");
    }

    [Fact]
    public async Task A_failed_validation_request_is_not_audited()
    {
        (await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Team",
            holder = "x",
            permissions = new[] { "orders:read" },
        })).IsSuccessStatusCode.Should().BeFalse();

        var rows = await ReadAuditRowsAsync();
        rows.Should().BeEmpty();
    }
}
