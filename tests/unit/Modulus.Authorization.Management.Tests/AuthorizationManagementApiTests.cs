using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Management;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

// Drives the admin API through a real TestServer host: authentication is
// enforced (the endpoints guard authorization data), writes land in the EF
// stores and are visible to the very next authorization decision, and invalid
// input comes back as RFC 7807 validation problems.
[Trait("Category", "Unit")]
public sealed class AuthorizationManagementApiTests : IAsyncLifetime
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
        builder.Services.AddModulusAuthorizationManagement();

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

    // Authenticates any request carrying X-Test-Authenticated. By default the
    // principal carries the authorization:manage permission claim the endpoint
    // policy requires; when X-Test-Roles is present the principal instead
    // carries only those role claims, so access must come from the grant store.
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
            };
            var roles = Request.Headers["X-Test-Roles"].ToString();
            if (roles.Length > 0)
                claims.AddRange(roles.Split(',')
                    .Select(role => new Claim(ClaimTypes.Role, role)));
            else
                claims.Add(new Claim(
                    "permission", AuthorizationManagementExtensions.ManagePermission));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        using var anonymous = _app.GetTestClient();
        var response = await anonymous.GetAsync("/authorization/delegations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Role_grant_in_the_ef_store_authorizes_without_a_permission_claim()
    {
        // A principal with only a role claim is denied until the store grants
        // the permission to that role — and allowed immediately afterwards,
        // proving HTTP authorization is server-resolved, not token-resolved.
        using var operators = _app.GetTestClient();
        operators.DefaultRequestHeaders.Add("X-Test-Authenticated", "yes");
        operators.DefaultRequestHeaders.Add("X-Test-Roles", "ops");

        (await operators.GetAsync("/authorization/delegations"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        await store.GrantToRoleAsync(
            "ops",
            [AuthorizationManagementExtensions.ManagePermission],
            CancellationToken.None);

        (await operators.GetAsync("/authorization/delegations"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Grant_lifecycle_lands_in_the_store_and_is_listable()
    {
        var post = await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "manager",
            permissions = new[] { "orders:read", "orders:update" },
            type = "Allow",
        });
        post.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listed = await _client.GetFromJsonAsync<GrantResponse[]>(
            "/authorization/grants/role/manager");
        listed.Should().HaveCount(2);

        // The write is visible to the authorization decision path immediately.
        var store = _app.Services.GetRequiredService<IPermissionGrantStore>();
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"]))
            .Should().HaveCount(2);

        var delete = await _client.DeleteAsync(
            "/authorization/grants/role/manager/orders:update");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<GrantResponse[]>(
            "/authorization/grants/role/manager"))
            .Should().ContainSingle()
            .Which.Permission.Should().Be("orders:read");
    }

    [Fact]
    public async Task Invalid_holder_type_is_a_400_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Team",
            holder = "x",
            permissions = new[] { "orders:read" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Role, User");
    }

    [Fact]
    public async Task Org_units_and_placements_round_trip_through_the_api()
    {
        var (root, team, userId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        (await _client.PostAsJsonAsync("/authorization/org/units",
            new { id = root }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsJsonAsync("/authorization/org/units",
            new { id = team, parents = new[] { root } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hierarchy = _app.Services.GetRequiredService<EfOrgHierarchy>();
        hierarchy.Descendants(root).Should().Contain(team);

        (await _client.PostAsJsonAsync("/authorization/org/placements",
            new { userId, orgUnitId = team, mode = "UnitOnly" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var placements = await _client.GetFromJsonAsync<List<PlacementView>>(
            $"/authorization/org/placements/{userId}");
        placements.Should().ContainSingle().Which.OrgUnitId.Should().Be(team);

        (await _client.DeleteAsync(
            $"/authorization/org/placements/{userId}/{team}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<List<PlacementView>>(
            $"/authorization/org/placements/{userId}"))
            .Should().BeEmpty();
    }

    private sealed record PlacementView(Guid UserId, Guid OrgUnitId, int Mode);

    [Fact]
    public async Task Entitlements_flow_through_plans_assignments_and_overrides()
    {
        var tenant = Guid.NewGuid();

        (await _client.PutAsJsonAsync("/authorization/features/plans/pro",
            new { features = new[] { "invoicing", "reporting" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync($"/authorization/features/tenants/{tenant}/plan",
            new { plan = "pro" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(
            $"/authorization/features/tenants/{tenant}/overrides/reporting",
            new { enabled = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var store = _app.Services.GetRequiredService<EfFeatureEntitlementStore>();
        store.AssignedPlan(tenant).Should().Be("pro");
        store.Override(tenant, "reporting").Should().BeFalse();

        (await _client.DeleteAsync(
            $"/authorization/features/tenants/{tenant}/overrides/reporting"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        store.Override(tenant, "reporting").Should().BeNull();

        (await _client.GetFromJsonAsync<string[]>("/authorization/features/plans/pro"))
            .Should().BeEquivalentTo(["invoicing", "reporting"]);
    }

    [Fact]
    public async Task Delegation_create_list_revoke_flow()
    {
        var now = DateTimeOffset.UtcNow;
        var create = await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = Guid.NewGuid(),
            fromRoles = new[] { "manager" },
            toUserId = Guid.NewGuid(),
            permissions = new[] { "orders:approve" },
            notBefore = now,
            notAfter = now.AddDays(7),
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<DelegationView>();

        (await _client.GetFromJsonAsync<List<DelegationView>>("/authorization/delegations"))
            .Should().ContainSingle().Which.Id.Should().Be(created!.Id);

        (await _client.DeleteAsync($"/authorization/delegations/{created.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/authorization/delegations/{created.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record DelegationView(Guid Id, Guid FromUserId, Guid ToUserId, bool Revoked);

    [Fact]
    public async Task Inverted_delegation_window_is_a_400_validation_problem()
    {
        var now = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = Guid.NewGuid(),
            fromRoles = Array.Empty<string>(),
            toUserId = Guid.NewGuid(),
            permissions = new[] { "x" },
            notBefore = now,
            notAfter = now,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("end after it begins");
    }
}
