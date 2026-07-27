using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Features;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

// Exercises the EF Core-backed authorization stores end-to-end against a real
// relational database (kept-open in-memory SQLite): registration supersedes the
// in-memory TryAdd defaults in either call order, mutations are durable and
// visible to the very next decision, and every store stays fail-closed when
// empty.
[Trait("Category", "Unit")]
public sealed class EfAuthorizationStoreTests : IDisposable
{
    // Fixed-clock TimeProvider (no extra test package): decision-time checks
    // and the hierarchy snapshot TTL both read this deterministic instant.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly TimeProvider _time = new FixedTimeProvider(
        new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));

    public EfAuthorizationStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_time);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));

        _provider = services.BuildServiceProvider();
        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    // ── Registration ───────────────────────────────────────────────

    [Fact]
    public void Supersedes_every_in_memory_default()
    {
        _provider.GetRequiredService<IPermissionGrantStore>()
            .Should().BeOfType<EfPermissionGrantStore>();
        _provider.GetRequiredService<IOrgHierarchy>()
            .Should().BeOfType<EfOrgHierarchy>();
        _provider.GetRequiredService<IOrgPlacementStore>()
            .Should().BeOfType<EfOrgPlacementStore>();
        _provider.GetRequiredService<IFeatureEntitlementStore>()
            .Should().BeOfType<EfFeatureEntitlementStore>();
        _provider.GetRequiredService<IDelegationStore>()
            .Should().BeOfType<EfDelegationStore>();
    }

    [Fact]
    public void Supersedes_defaults_when_registered_before_AddModulusAuthorization()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(connection));
        services.AddModulusAuthorization();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPermissionGrantStore>()
            .Should().BeOfType<EfPermissionGrantStore>();
        provider.GetRequiredService<IDelegationStore>()
            .Should().BeOfType<EfDelegationStore>();
    }

    // ── Grants ─────────────────────────────────────────────────────

    [Fact]
    public async Task Grants_round_trip_for_roles_and_user()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();
        var userId = Guid.NewGuid();

        await store.GrantToRoleAsync("manager", ["orders:read", "orders:update"]);
        await store.DenyToRoleAsync("manager", ["orders:delete"]);
        await store.GrantToUserAsync(userId, ["reports:view"]);

        var grants = store.GetGrants(new PrincipalGrantQuery(userId, ["manager"]));

        grants.Should().HaveCount(4);
        grants.Should().ContainSingle(g =>
            g.Permission == "orders:delete" && g.Type == PermissionGrantType.Deny);
        grants.Should().ContainSingle(g =>
            g.Permission == "reports:view" && g.HolderType == GrantHolderType.User);
    }

    [Fact]
    public async Task Regranting_the_same_permission_replaces_the_grant_type()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        await store.GrantToRoleAsync("clerk", ["orders:approve"]);
        await store.DenyToRoleAsync("clerk", ["orders:approve"]);

        var grants = store.GetGrants(new PrincipalGrantQuery(null, ["clerk"]));
        grants.Should().ContainSingle()
            .Which.Type.Should().Be(PermissionGrantType.Deny);
    }

    [Fact]
    public async Task Revoked_grant_disappears_from_the_next_decision()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        await store.GrantToRoleAsync("temp", ["orders:read"]);
        await store.RevokeFromRoleAsync("temp", "orders:read");

        store.GetGrants(new PrincipalGrantQuery(null, ["temp"])).Should().BeEmpty();
    }

    [Fact]
    public void Empty_store_is_fail_closed()
    {
        var store = _provider.GetRequiredService<IPermissionGrantStore>();
        store.GetGrants(new PrincipalGrantQuery(Guid.NewGuid(), ["anything"]))
            .Should().BeEmpty();
        store.GetGrants(PrincipalGrantQuery.Anonymous).Should().BeEmpty();
    }

    // ── Org hierarchy + placements ─────────────────────────────────

    [Fact]
    public async Task Hierarchy_closures_are_durable_and_transitive()
    {
        var hierarchy = _provider.GetRequiredService<EfOrgHierarchy>();
        var (root, region, branch) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await hierarchy.AddUnitAsync(root, []);
        await hierarchy.AddUnitAsync(region, [root]);
        await hierarchy.AddUnitAsync(branch, [region]);

        hierarchy.Contains(root).Should().BeTrue();
        hierarchy.Descendants(root).Should().BeEquivalentTo([region, branch]);
        hierarchy.Ancestors(branch).Should().BeEquivalentTo([region, root]);
    }

    [Fact]
    public async Task MoveUnit_reorg_is_visible_immediately()
    {
        var hierarchy = _provider.GetRequiredService<EfOrgHierarchy>();
        var (rootA, rootB, team) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await hierarchy.AddUnitAsync(rootA, []);
        await hierarchy.AddUnitAsync(rootB, []);
        await hierarchy.AddUnitAsync(team, [rootA]);
        hierarchy.Descendants(rootA).Should().Contain(team);

        await hierarchy.MoveUnitAsync(team, [rootB]);

        hierarchy.Descendants(rootA).Should().BeEmpty();
        hierarchy.Descendants(rootB).Should().Contain(team);
    }

    [Fact]
    public void Unknown_unit_is_fail_closed()
    {
        var hierarchy = _provider.GetRequiredService<IOrgHierarchy>();
        hierarchy.Contains(Guid.NewGuid()).Should().BeFalse();
        hierarchy.Descendants(Guid.NewGuid()).Should().BeEmpty();
        hierarchy.Ancestors(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task Self_parent_is_rejected()
    {
        var hierarchy = _provider.GetRequiredService<EfOrgHierarchy>();
        var id = Guid.NewGuid();

        var act = () => hierarchy.AddUnitAsync(id, [id]);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Placements_round_trip_and_replacing_updates_the_mode()
    {
        var store = _provider.GetRequiredService<EfOrgPlacementStore>();
        var (userId, unitId) = (Guid.NewGuid(), Guid.NewGuid());

        await store.PlaceAsync(userId, unitId, OrgScopeMode.UnitOnly);
        store.GetPlacements(userId).Should().ContainSingle()
            .Which.Mode.Should().Be(OrgScopeMode.UnitOnly);

        await store.PlaceAsync(userId, unitId, OrgScopeMode.UnitAndDescendants);
        store.GetPlacements(userId).Should().ContainSingle()
            .Which.Mode.Should().Be(OrgScopeMode.UnitAndDescendants);

        await store.RemoveAsync(userId, unitId);
        store.GetPlacements(userId).Should().BeEmpty();
    }

    // ── Feature entitlements ───────────────────────────────────────

    [Fact]
    public async Task Entitlements_plan_assignment_and_overrides_round_trip()
    {
        var store = _provider.GetRequiredService<EfFeatureEntitlementStore>();
        var tenant = Guid.NewGuid();

        await store.DefinePlanAsync("pro", ["invoicing", "reporting"]);
        await store.AssignPlanAsync(tenant, "pro");
        await store.EnableAsync(tenant, "ai-copilot");
        await store.DisableAsync(tenant, "reporting");

        store.PlanFeatures("pro").Should().BeEquivalentTo(["invoicing", "reporting"]);
        store.AssignedPlan(tenant).Should().Be("pro");
        store.Override(tenant, "ai-copilot").Should().BeTrue();
        store.Override(tenant, "reporting").Should().BeFalse();
        store.Override(tenant, "invoicing").Should().BeNull();
        store.Overrides(tenant).Should().HaveCount(2);

        await store.ClearOverrideAsync(tenant, "reporting");
        store.Override(tenant, "reporting").Should().BeNull();
    }

    [Fact]
    public async Task Redefining_a_plan_replaces_its_feature_bundle()
    {
        var store = _provider.GetRequiredService<EfFeatureEntitlementStore>();

        await store.DefinePlanAsync("basic", ["invoicing", "reporting"]);
        await store.DefinePlanAsync("basic", ["invoicing"]);

        store.PlanFeatures("basic").Should().BeEquivalentTo(["invoicing"]);
    }

    [Fact]
    public void Unknown_plan_and_tenant_are_fail_closed()
    {
        var store = _provider.GetRequiredService<IFeatureEntitlementStore>();
        store.PlanFeatures("nope").Should().BeEmpty();
        store.AssignedPlan(Guid.NewGuid()).Should().BeNull();
        store.Overrides(Guid.NewGuid()).Should().BeEmpty();
    }

    // ── Delegations ────────────────────────────────────────────────

    [Fact]
    public async Task Delegation_is_active_only_inside_its_window()
    {
        var store = _provider.GetRequiredService<EfDelegationStore>();
        var (from, to) = (Guid.NewGuid(), Guid.NewGuid());
        var now = _time.GetUtcNow();

        await store.DelegateAsync(
            from, ["manager"], to, ["orders:approve"],
            notBefore: now, notAfter: now.AddDays(7));

        store.ActiveFor(to, now).Should().ContainSingle()
            .Which.Permissions.Should().Contain("orders:approve");
        store.ActiveFor(to, now.AddDays(8)).Should().BeEmpty();
        store.ActiveFor(to, now.AddSeconds(-1)).Should().BeEmpty();
        store.ActiveFor(Guid.NewGuid(), now).Should().BeEmpty();
    }

    [Fact]
    public async Task Revocation_is_immediate_and_idempotent()
    {
        var store = _provider.GetRequiredService<EfDelegationStore>();
        var (from, to) = (Guid.NewGuid(), Guid.NewGuid());
        var now = _time.GetUtcNow();

        var delegation = await store.DelegateAsync(
            from, ["manager"], to, ["orders:approve"],
            notBefore: now, notAfter: now.AddDays(7));

        (await store.RevokeAsync(delegation.Id)).Should().BeTrue();
        store.ActiveFor(to, now).Should().BeEmpty();
        (await store.RevokeAsync(delegation.Id)).Should().BeFalse();

        // Revoked delegations remain visible to governance review.
        store.All().Should().ContainSingle().Which.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task Delegation_role_snapshot_and_permissions_survive_the_round_trip()
    {
        var store = _provider.GetRequiredService<EfDelegationStore>();
        var now = _time.GetUtcNow();

        await store.DelegateAsync(
            Guid.NewGuid(), ["cfo", "approver"], Guid.NewGuid(),
            ["payments:release", "payments:approve"],
            notBefore: now, notAfter: now.AddHours(4));

        var stored = store.All().Should().ContainSingle().Subject;
        stored.FromRoles.Should().BeEquivalentTo(["cfo", "approver"]);
        stored.Permissions.Should().BeEquivalentTo(["payments:release", "payments:approve"]);
    }

    [Fact]
    public async Task Inverted_delegation_window_is_rejected()
    {
        var store = _provider.GetRequiredService<EfDelegationStore>();
        var now = _time.GetUtcNow();

        var act = () => store.DelegateAsync(
            Guid.NewGuid(), [], Guid.NewGuid(), ["x"],
            notBefore: now, notAfter: now);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
