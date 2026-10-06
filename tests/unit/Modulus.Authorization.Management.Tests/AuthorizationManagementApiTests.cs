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
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Scopes;
using Modulus.Authorization.Management;
using Modulus.Core.Abstractions;
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
    private readonly FakeRoleDirectory _directory = new();
    private readonly FakeTenant _tenant = new();

    private sealed class FakeRoleDirectory : IUserRoleDirectory
    {
        private readonly Dictionary<Guid, string[]> _users = [];

        public Guid AddUser(params string[] roles)
        {
            var id = Guid.NewGuid();
            _users[id] = roles;
            return id;
        }

        public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyCollection<string>?>(_users.TryGetValue(userId, out var roles) ? roles : null);
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public Guid? TenantId { get; set; }
        public string? TenantSlug => null;
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; set; } = true;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

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
        builder.Services.AddPermissions("Orders", registry =>
        {
            registry.Add("orders:read", "Read orders.");
            registry.Add("orders:update", "Update orders.");
            registry.Add("orders:approve", "Approve orders.");
            registry.Add("orders:create", "Create orders.");
        });
        builder.Services.AddSegregationOfDuties(new SodConstraint(
            "maker-checker", ["orders:create", "orders:approve"], "Whoever creates an order must not approve it."));
        builder.Services.AddSingleton(_directory);
        builder.Services.AddSingleton<IUserRoleDirectory>(_directory);
        builder.Services.AddSingleton<ICurrentTenant>(_tenant);

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

            var userId = Request.Headers["X-Test-UserId"].ToString();
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.Length > 0 ? userId : Guid.NewGuid().ToString()),
            };
            var roles = Request.Headers["X-Test-Roles"].ToString();
            var permissions = Request.Headers["X-Test-Permissions"].ToString();
            if (roles.Length > 0)
                claims.AddRange(roles.Split(',')
                    .Select(role => new Claim(ClaimTypes.Role, role)));
            else if (permissions.Length > 0)
                claims.AddRange(permissions.Split(',').Select(p => new Claim("permission", p)));
            else
                claims.AddRange(new[]
                {
                    AuthorizationManagementExtensions.ManagePermission,
                    AuthorizationManagementExtensions.EntitlementsPermission,
                    "orders:read", "orders:update", "orders:approve", "orders:create",
                }.Select(p => new Claim("permission", p)));

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
        var from = _directory.AddUser("manager");
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        await store.GrantToRoleAsync("manager", ["orders:approve"], CancellationToken.None);
        var create = await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = from,
            toUserId = _directory.AddUser(),
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
    public async Task Delegation_with_empty_permissions_is_a_400_validation_problem()
    {
        var now = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = Guid.NewGuid(),
            fromRoles = new[] { "manager" },
            toUserId = Guid.NewGuid(),
            permissions = Array.Empty<string>(),
            notBefore = now,
            notAfter = now.AddDays(1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("At least one permission is required");
    }

    [Fact]
    public async Task Self_delegation_is_a_400_validation_problem()
    {
        var now = DateTimeOffset.UtcNow;
        var self = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = self,
            fromRoles = new[] { "manager" },
            toUserId = self,
            permissions = new[] { "orders:approve" },
            notBefore = now,
            notAfter = now.AddDays(1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("different user");
    }

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

    private HttpClient As(string permissions, Guid? userId = null)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "yes");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (userId is { } id)
            client.DefaultRequestHeaders.Add("X-Test-UserId", id.ToString());
        return client;
    }

    // ── Grants: only catalog permissions, only what you hold, never to yourself ──

    [Fact]
    public async Task Granting_an_unregistered_permission_is_refused()
    {
        var response = await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "manager",
            permissions = new[] { "orders:aprove" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("orders:aprove");
    }

    [Fact]
    public async Task A_wildcard_that_matches_nothing_is_refused()
    {
        var response = await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "manager",
            permissions = new[] { "payroll:*" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_administrator_cannot_grant_what_they_do_not_hold()
    {
        using var admin = As("authorization:manage,orders:read");

        var refused = await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "clerk",
            permissions = new[] { "orders:read", "orders:approve" },
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("orders:approve").And.NotContain("orders:read,");

        var allowed = await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "clerk",
            permissions = new[] { "orders:read" },
        });
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_wildcard_grant_is_checked_against_everything_it_expands_to()
    {
        using var admin = As("authorization:manage,orders:read");

        (await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "clerk",
            permissions = new[] { "orders:*" },
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Grant_any_lifts_the_ceiling()
    {
        using var admin = As("authorization:manage,authorization:grant-any");

        (await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "clerk",
            permissions = new[] { "orders:approve" },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Nobody_grants_to_themselves_even_with_grant_any()
    {
        var me = Guid.NewGuid();
        using var admin = As("authorization:manage,authorization:grant-any", me);

        (await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "User",
            holder = me.ToString(),
            permissions = new[] { "orders:approve" },
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nobody_lifts_a_denial_that_applies_to_them()
    {
        var me = Guid.NewGuid();
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        await store.DenyToUserAsync(me, ["orders:approve"], CancellationToken.None);
        using var admin = As("authorization:manage", me);

        (await admin.DeleteAsync($"/authorization/grants/user/{me}/orders:approve"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await store.GetGrantsForHolderAsync(GrantHolderType.User, me.ToString()))
            .Should().ContainSingle(g => g.Type == PermissionGrantType.Deny);
    }

    [Fact]
    public async Task A_refused_grant_is_not_applied()
    {
        using var admin = As("authorization:manage");
        await admin.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "clerk",
            permissions = new[] { "orders:approve" },
        });

        (await _client.GetFromJsonAsync<GrantResponse[]>("/authorization/grants/role/clerk")).Should().BeEmpty();
    }

    // ── SoD: role grants are checked, and so are the roles the identity store reports ──

    [Fact]
    public async Task A_role_grant_that_completes_a_toxic_pair_is_refused()
    {
        (await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "buyer",
            permissions = new[] { "orders:create" },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "Role",
            holder = "buyer",
            permissions = new[] { "orders:approve" },
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_user_grant_is_checked_against_the_roles_the_directory_reports_not_the_request()
    {
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        await store.GrantToRoleAsync("buyer", ["orders:create"], CancellationToken.None);
        var user = _directory.AddUser("buyer");

        // The caller claims the user has no roles; the identity store says otherwise.
        var response = await _client.PostAsJsonAsync("/authorization/grants", new
        {
            holderType = "User",
            holder = user.ToString(),
            permissions = new[] { "orders:approve" },
            holderRoles = Array.Empty<string>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Platform-wide entitlements are host-only ──

    [Fact]
    public async Task A_company_administrator_cannot_touch_platform_entitlements()
    {
        using var admin = As("authorization:manage");

        (await admin.PutAsJsonAsync("/authorization/features/plans/pro", new { features = new[] { "x" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/authorization/features/tenants/{Guid.NewGuid()}/plan", new { plan = "pro" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Entitlements_are_refused_inside_a_company_even_for_the_permission_holder()
    {
        _tenant.IsHost = false;
        _tenant.TenantId = Guid.NewGuid();

        (await _client.PutAsJsonAsync("/authorization/features/plans/pro", new { features = new[] { "x" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Delegation: capped by what the server knows, not what the caller says ──

    private async Task<HttpResponseMessage> DelegateAsync(Guid from, string[] permissions, TimeSpan? length = null, string[]? claimedRoles = null)
    {
        var now = DateTimeOffset.UtcNow;
        return await _client.PostAsJsonAsync("/authorization/delegations", new
        {
            fromUserId = from,
            fromRoles = claimedRoles ?? Array.Empty<string>(),
            toUserId = _directory.AddUser(),
            permissions,
            notBefore = now,
            notAfter = now + (length ?? TimeSpan.FromDays(3)),
        });
    }

    [Fact]
    public async Task A_delegator_cannot_lend_authority_they_do_not_hold_whatever_roles_the_request_claims()
    {
        var from = _directory.AddUser("clerk");

        var response = await DelegateAsync(from, ["orders:approve"], claimedRoles: ["manager"]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("does not hold");
    }

    [Fact]
    public async Task A_delegation_is_capped_by_the_delegators_real_roles()
    {
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        await store.GrantToRoleAsync("manager", ["orders:approve"], CancellationToken.None);
        var from = _directory.AddUser("manager");

        (await DelegateAsync(from, ["orders:approve"])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Security_administration_cannot_be_delegated()
    {
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        var from = _directory.AddUser();
        await store.GrantToUserAsync(from, ["authorization:manage"], CancellationToken.None);

        var response = await DelegateAsync(from, ["authorization:manage"]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Not delegable");
    }

    [Fact]
    public async Task A_delegation_longer_than_the_limit_is_refused()
    {
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        var from = _directory.AddUser();
        await store.GrantToUserAsync(from, ["orders:approve"], CancellationToken.None);

        (await DelegateAsync(from, ["orders:approve"], TimeSpan.FromDays(90))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delegating_to_or_from_an_unknown_user_is_refused()
    {
        (await DelegateAsync(Guid.NewGuid(), ["orders:approve"])).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Scoped, temporary and restricting grants ──

    private Task<HttpResponseMessage> PostScoped(HttpClient client, object body) => client.PostAsJsonAsync("/authorization/scoped-grants", body);

    private sealed record ScopedView(Guid Id, string Permission, string Type, string Scope, DateTimeOffset? ValidUntil, string? Reason);

    [Fact]
    public async Task A_scoped_grant_is_created_listed_and_removed()
    {
        var created = await PostScoped(_client, new
        {
            holderType = "Role",
            holder = "rep",
            permission = "orders:read",
            scope = "own",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var listed = await _client.GetFromJsonAsync<ScopedView[]>("/authorization/scoped-grants/role/rep");
        var grant = listed.Should().ContainSingle().Subject;
        (grant.Type, grant.Scope).Should().Be(("Allow", "own"));

        (await _client.DeleteAsync($"/authorization/scoped-grants/{grant.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/authorization/scoped-grants/{grant.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetFromJsonAsync<ScopedView[]>("/authorization/scoped-grants/role/rep")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("org:not-a-guid")]
    [InlineData("assigned:")]
    public async Task A_malformed_scope_is_refused(string scope)
        => (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:read", scope }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_scope_naming_an_unknown_org_unit_is_refused()
        => (await PostScoped(_client, new
        {
            holderType = "Role",
            holder = "rep",
            permission = "orders:read",
            scope = $"org:{Guid.NewGuid()}",
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_scoped_grant_obeys_the_catalog_the_ceiling_and_the_self_grant_rule()
    {
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:nope", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:*", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var limited = As("authorization:manage,orders:read");
        (await PostScoped(limited, new { holderType = "Role", holder = "rep", permission = "orders:approve", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var me = Guid.NewGuid();
        using var selfGranting = As("authorization:manage,authorization:grant-any", me);
        (await PostScoped(selfGranting, new { holderType = "User", holder = me.ToString(), permission = "orders:read", scope = "tenant" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_temporary_grant_needs_a_future_end_a_reason_and_a_sane_length()
    {
        var soon = DateTimeOffset.UtcNow.AddDays(3);

        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:approve", validUntil = soon }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:approve", validUntil = DateTimeOffset.UtcNow.AddDays(-1), reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:approve", validUntil = DateTimeOffset.UtcNow.AddDays(400), reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:approve", validUntil = soon, reason = "cover for Dana" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var grant = (await _client.GetFromJsonAsync<ScopedView[]>("/authorization/scoped-grants/role/rep")).Should().ContainSingle().Subject;
        grant.Reason.Should().Be("cover for Dana");
        grant.ValidUntil.Should().BeCloseTo(soon, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_restriction_needs_a_narrower_scope_and_a_deny_takes_none()
    {
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:read", type = "Restrict", scope = "tenant" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:read", type = "Deny", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostScoped(_client, new { holderType = "Role", holder = "rep", permission = "orders:read", type = "Restrict", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_scoped_grant_that_completes_a_toxic_pair_is_refused()
    {
        (await PostScoped(_client, new { holderType = "Role", holder = "buyer", permission = "orders:create", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await PostScoped(_client, new { holderType = "Role", holder = "buyer", permission = "orders:approve", scope = "own" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nobody_lifts_a_restriction_that_applies_to_them()
    {
        var me = Guid.NewGuid();
        var store = _app.Services.GetRequiredService<EfPermissionGrantStore>();
        var restriction = await store.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, me.ToString(), "orders:read", PermissionGrantType.Restrict, PermissionScope.Own), null, DateTimeOffset.UtcNow);
        using var admin = As("authorization:manage", me);

        (await admin.DeleteAsync($"/authorization/scoped-grants/{restriction.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.DeleteAsync($"/authorization/scoped-grants/{restriction.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Assignments ──

    [Fact]
    public async Task Assignments_round_trip_through_the_api_and_reject_unknown_users()
    {
        var user = _directory.AddUser();
        var target = Guid.NewGuid();

        (await _client.PostAsJsonAsync("/authorization/assignments", new { userId = user, assignmentType = "Customer", targetId = target }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<Assignment[]>($"/authorization/assignments/{user}"))
            .Should().ContainSingle().Which.TargetId.Should().Be(target);

        (await _client.PostAsJsonAsync("/authorization/assignments", new { userId = Guid.NewGuid(), assignmentType = "customer", targetId = target }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsJsonAsync("/authorization/assignments", new { userId = user, assignmentType = " ", targetId = target }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _client.DeleteAsync($"/authorization/assignments/{user}/customer/{target}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/authorization/assignments/{user}/customer/{target}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
