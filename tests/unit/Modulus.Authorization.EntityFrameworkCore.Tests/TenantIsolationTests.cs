using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// B4: <see cref="AuthorizationStoreDbContext"/> used to have no <c>TenantId</c>
/// at all on grants, org structure, or delegations, and derived from a plain
/// <see cref="DbContext"/> with no query filter — any <c>authorization:manage</c>
/// holder in any tenant could read and write every other tenant's authorization
/// data through <c>MapModulusAuthorizationManagement</c>. This asserts the fix:
/// a resolved tenant sees only its own rows, the host sees everyone, and an
/// unresolved tenant sees nothing — the same fail-closed shape
/// <c>Modulus.Identity.Tests.TenantIsolationTests</c> already proved for
/// <c>ModulusIdentityDbContext</c> (the identical bug, B2, fixed earlier).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FakeCurrentTenant _tenant = new();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public TenantIsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
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

    // ── Grants ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_grant_created_in_one_tenant_is_invisible_from_another()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        _tenant.EnterTenant(_tenantA);
        await store.GrantToRoleAsync("manager", ["orders:approve"]);

        _tenant.EnterTenant(_tenantB);
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"])).Should().BeEmpty();
        (await store.GetGrantsForHolderAsync(GrantHolderType.Role, "manager")).Should().BeEmpty();

        _tenant.EnterTenant(_tenantA);
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"])).Should().ContainSingle();
    }

    [Fact]
    public async Task Host_sees_grants_from_every_tenant()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        _tenant.EnterTenant(_tenantA);
        await store.GrantToRoleAsync("manager", ["orders:approve"]);
        _tenant.EnterTenant(_tenantB);
        await store.GrantToRoleAsync("manager", ["invoices:view"]);

        _tenant.EnterHost();
        var grants = store.GetGrants(new PrincipalGrantQuery(null, ["manager"]));
        grants.Select(g => g.Permission).Should().BeEquivalentTo("orders:approve", "invoices:view");
    }

    [Fact]
    public async Task Unresolved_tenant_sees_nothing_not_everyone()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        _tenant.EnterTenant(_tenantA);
        await store.GrantToRoleAsync("manager", ["orders:approve"]);

        _tenant.EnterUnresolved();
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"])).Should().BeEmpty();
    }

    [Fact]
    public async Task Same_role_name_granted_differently_in_two_tenants_does_not_collide()
    {
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();

        _tenant.EnterTenant(_tenantA);
        await store.GrantToRoleAsync("manager", ["orders:approve"]);
        _tenant.EnterTenant(_tenantB);
        await store.DenyToRoleAsync("manager", ["orders:approve"]);

        _tenant.EnterTenant(_tenantA);
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"])).Should()
            .ContainSingle().Which.Type.Should().Be(PermissionGrantType.Allow);

        _tenant.EnterTenant(_tenantB);
        store.GetGrants(new PrincipalGrantQuery(null, ["manager"])).Should()
            .ContainSingle().Which.Type.Should().Be(PermissionGrantType.Deny);
    }

    // ── Org placements ────────────────────────────────────────────

    [Fact]
    public async Task Placements_are_isolated_per_tenant()
    {
        var store = _provider.GetRequiredService<EfOrgPlacementStore>();
        var (userA, unit) = (Guid.NewGuid(), Guid.NewGuid());

        _tenant.EnterTenant(_tenantA);
        await store.PlaceAsync(userA, unit, OrgScopeMode.UnitOnly);

        _tenant.EnterTenant(_tenantB);
        store.GetPlacements(userA).Should().BeEmpty();

        _tenant.EnterHost();
        store.GetPlacements(userA).Should().ContainSingle();

        _tenant.EnterUnresolved();
        store.GetPlacements(userA).Should().BeEmpty();
    }

    // ── Delegations ────────────────────────────────────────────────

    [Fact]
    public async Task Delegations_are_isolated_per_tenant()
    {
        var store = _provider.GetRequiredService<EfDelegationStore>();
        var (from, to) = (Guid.NewGuid(), Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        _tenant.EnterTenant(_tenantA);
        await store.DelegateAsync(from, ["manager"], to, ["orders:approve"], now, now.AddDays(1));

        _tenant.EnterTenant(_tenantB);
        store.ActiveFor(to, now).Should().BeEmpty();
        store.All().Should().BeEmpty();

        _tenant.EnterHost();
        store.All().Should().ContainSingle();

        _tenant.EnterUnresolved();
        store.ActiveFor(to, now).Should().BeEmpty();
        store.All().Should().BeEmpty();

        _tenant.EnterTenant(_tenantA);
        store.ActiveFor(to, now).Should().ContainSingle();
    }

    // ── Org hierarchy (proves the per-tenant cache partition works) ──

    [Fact]
    public async Task Hierarchy_is_isolated_per_tenant_and_the_cache_does_not_leak_across_tenants()
    {
        var hierarchy = _provider.GetRequiredService<EfOrgHierarchy>();
        var (root, child) = (Guid.NewGuid(), Guid.NewGuid());

        _tenant.EnterTenant(_tenantA);
        await hierarchy.AddUnitAsync(root, []);
        await hierarchy.AddUnitAsync(child, [root]);

        // Tenant A sees the real graph.
        hierarchy.Contains(root).Should().BeTrue();
        hierarchy.Descendants(root).Should().ContainSingle().Which.Should().Be(child);

        // Tenant B must never see tenant A's units, even though EfOrgHierarchy
        // is a singleton whose cache is now warm from tenant A's refresh above
        // — proves the cache is keyed per tenant, not shared.
        _tenant.EnterTenant(_tenantB);
        hierarchy.Contains(root).Should().BeFalse();
        hierarchy.Descendants(root).Should().BeEmpty();

        // Host sees the union.
        _tenant.EnterHost();
        hierarchy.Contains(root).Should().BeTrue();
        hierarchy.Descendants(root).Should().ContainSingle();

        // Unresolved sees nothing (fail-closed), not the host's or any
        // tenant's cached graph.
        _tenant.EnterUnresolved();
        hierarchy.Contains(root).Should().BeFalse();
        hierarchy.Descendants(root).Should().BeEmpty();

        // Back to tenant A: its cache entry survived the intervening
        // host/unresolved/tenant-B reads untouched.
        _tenant.EnterTenant(_tenantA);
        hierarchy.Contains(root).Should().BeTrue();
    }

    /// <summary>Mutable <see cref="ICurrentTenant"/> test double: flips scope
    /// between calls on the same singleton stores, like the real ambient
    /// tenant does across requests.</summary>
    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost { get; private set; }

        public void EnterHost()
        {
            IsHost = true;
            TenantId = null;
        }

        public void EnterTenant(Guid tenantId)
        {
            IsHost = false;
            TenantId = tenantId;
        }

        public void EnterUnresolved()
        {
            IsHost = false;
            TenantId = null;
        }

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException("Not exercised by these tests.");
    }
}
