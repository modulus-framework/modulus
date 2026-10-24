using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// A grant or a delegation held by a parent company applies in the companies below it: a company reads its own rows and those of the
/// companies above it. A deny at an ancestor applies in the child (deny wins in the checker); a child's row never reaches its parent. The
/// synchronous and asynchronous reads agree.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ParentGrantTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FakeCurrentTenant _tenant;
    private readonly Guid _holding = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _unrelated = Guid.NewGuid();

    public ParentGrantTests()
    {
        _tenant = new FakeCurrentTenant(new Dictionary<Guid, Guid?> { [_branch] = _holding, [_holding] = null, [_unrelated] = null });
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private Modulus.Authorization.EntityFrameworkCore.EfPermissionGrantStore Store()
        => _provider.GetRequiredService<Modulus.Authorization.EntityFrameworkCore.EfPermissionGrantStore>();

    private Task<IReadOnlyCollection<PermissionGrant>> ReadAsync(string role)
        => Store().GetGrantsAsync(new PrincipalGrantQuery(null, [role]));

    [Fact]
    public async Task A_grant_held_by_the_parent_applies_in_the_child()
    {
        _tenant.EnterTenant(_holding);
        await Store().GrantToRoleAsync("manager", ["orders:read"]);

        _tenant.EnterTenant(_branch);
        (await ReadAsync("manager")).Should().ContainSingle(g => g.Permission == "orders:read" && g.Type == PermissionGrantType.Allow);
    }

    [Fact]
    public async Task A_grant_held_by_the_child_does_not_reach_its_parent_or_an_unrelated_company()
    {
        _tenant.EnterTenant(_branch);
        await Store().GrantToRoleAsync("manager", ["orders:read"]);

        _tenant.EnterTenant(_holding);
        (await ReadAsync("manager")).Should().BeEmpty("access does not climb the tree");
        _tenant.EnterTenant(_unrelated);
        (await ReadAsync("manager")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_deny_at_the_parent_is_read_in_the_child_so_it_wins_over_the_childs_allow()
    {
        _tenant.EnterTenant(_holding);
        await Store().DenyToRoleAsync("manager", ["orders:approve"]);
        _tenant.EnterTenant(_branch);
        await Store().GrantToRoleAsync("manager", ["orders:approve"]);

        var grants = await ReadAsync("manager");
        grants.Should().Contain(g => g.Permission == "orders:approve" && g.Type == PermissionGrantType.Deny, "the parent's deny always applies below it");
        grants.Should().Contain(g => g.Permission == "orders:approve" && g.Type == PermissionGrantType.Allow);
    }

    [Fact]
    public async Task The_synchronous_and_asynchronous_reads_agree_for_a_child()
    {
        _tenant.EnterTenant(_holding);
        await Store().GrantToRoleAsync("manager", ["orders:read"]);
        _tenant.EnterTenant(_branch);
        await Store().GrantToRoleAsync("manager", ["orders:approve"]);

        var query = new PrincipalGrantQuery(null, ["manager"]);
        var sync = Store().GetGrants(query).Select(g => (g.Permission, g.Type)).OrderBy(g => g.Permission).ToList();
        var async = (await Store().GetGrantsAsync(query)).Select(g => (g.Permission, g.Type)).OrderBy(g => g.Permission).ToList();
        sync.Should().Equal(async);
        sync.Select(g => g.Permission).Should().Contain(["orders:approve", "orders:read"]);
    }

    [Fact]
    public async Task A_delegation_made_in_the_parent_is_in_force_in_the_child()
    {
        var delegations = _provider.GetRequiredService<Modulus.Authorization.EntityFrameworkCore.EfDelegationStore>();
        var now = DateTimeOffset.UtcNow;
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        _tenant.EnterTenant(_holding);
        await delegations.DelegateAsync(from, ["manager"], to, ["orders:read"], now.AddMinutes(-1), now.AddHours(1));

        _tenant.EnterTenant(_branch);
        delegations.ActiveFor(to, now).Should().ContainSingle(d => d.Permissions.Contains("orders:read"));
        _tenant.EnterTenant(_unrelated);
        delegations.ActiveFor(to, now).Should().BeEmpty();
    }

    [Fact]
    public async Task A_child_lists_the_grants_it_inherits_tagged_with_the_company_that_holds_them()
    {
        var store = Store();
        _tenant.EnterTenant(_holding);
        await store.GrantToRoleAsync("manager", ["orders:read"]);
        _tenant.EnterTenant(_branch);
        await store.GrantToRoleAsync("manager", ["orders:approve"]);

        var inherited = await store.GetInheritedGrantsForHolderAsync(GrantHolderType.Role, "manager");
        inherited.Should().ContainSingle()
            .Which.Should().Match<InheritedGrant>(i => i.CompanyId == _holding && i.Grant.Permission == "orders:read");

        _tenant.EnterTenant(_holding);
        (await store.GetInheritedGrantsForHolderAsync(GrantHolderType.Role, "manager")).Should().BeEmpty("the top company inherits nothing");
    }

    [Fact]
    public async Task An_unresolved_company_reads_nothing()
    {
        _tenant.EnterTenant(_holding);
        await Store().GrantToRoleAsync("manager", ["orders:read"]);

        _tenant.EnterUnresolved();
        (await ReadAsync("manager")).Should().BeEmpty();
    }

    /// <summary>A tenant whose lineage follows a fixed tree: each company maps to its parent (null for the top).</summary>
    private sealed class FakeCurrentTenant(IReadOnlyDictionary<Guid, Guid?> parents) : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost { get; private set; }

        public IReadOnlyList<Guid> Lineage
        {
            get
            {
                var chain = new List<Guid>();
                Guid? cursor = TenantId;
                while (cursor is { } current && chain.Count < 64 && !chain.Contains(current))
                {
                    chain.Add(current);
                    cursor = parents.TryGetValue(current, out var parent) ? parent : null;
                }
                return chain;
            }
        }

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
