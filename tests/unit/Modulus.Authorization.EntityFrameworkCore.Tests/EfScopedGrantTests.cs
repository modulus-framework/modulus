using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>Scoped, temporary and restricting grants and assignments are durable, and a scope becomes a real SQL predicate.</summary>
[Trait("Category", "Unit")]
public sealed class EfScopedGrantTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Acme = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid Globex = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;
    private readonly TestClock _clock = new(Now);
    private readonly MutableUser _user = new();

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private sealed class MutableUser : ICurrentUser, IPrincipalGrantQuerySource
    {
        public Guid? Id { get; set; }
        public string[] Roles { get; set; } = [];
        public Guid? UserId => Id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Id is not null;
        public bool IsInRole(string role) => Roles.Contains(role);
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
        public PrincipalGrantQuery Current => Id is null ? PrincipalGrantQuery.Anonymous : new PrincipalGrantQuery(Id, Roles);
    }

    private sealed class Order : IHasOwner
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid CustomerId { get; set; }
    }

    private sealed class SalesDb(DbContextOptions<SalesDb> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    public EfScopedGrantTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_user);
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableUser>());
        services.AddScoped<IPrincipalGrantQuerySource>(sp => sp.GetRequiredService<MutableUser>());
        services.AddModulusAuthorization();
        services.AddPermissions("Sales", r =>
        {
            r.Add("orders:read", "Read orders.");
            r.Add("orders:approve", "Approve orders.");
        });
        services.AddScopeMap<Order>(m => m.Owner(o => o.OwnerId).AssignedKey("customer", o => o.CustomerId));
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        services.AddDbContext<SalesDb>(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        using var sales = _provider.CreateScope().ServiceProvider.GetRequiredService<SalesDb>();
        // Same database as the authorization store, so EnsureCreated would be a no-op: create this context's tables directly.
        ((Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)sales.Database
            .GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>()).CreateTables();
        sales.Orders.AddRange(
            new Order { Id = Guid.NewGuid(), OwnerId = Alice, CustomerId = Acme },
            new Order { Id = Guid.NewGuid(), OwnerId = Bob, CustomerId = Globex },
            new Order { Id = Guid.NewGuid(), OwnerId = Bob, CustomerId = Acme });
        sales.SaveChanges();

        // The permission catalog is replayed by a hosted service at startup; there is no host here.
        var registry = _provider.GetRequiredService<IPermissionRegistry>();
        foreach (var registration in _provider.GetServices<IPermissionRegistration>())
            registration.Apply(registry);
        registry.Freeze();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private EfPermissionGrantStore Grants => _provider.GetRequiredService<EfPermissionGrantStore>();

    [Fact]
    public async Task A_scoped_grant_is_stored_and_returned_with_its_scope_and_window()
    {
        var saved = await Grants.AddScopedGrantAsync(
            new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow,
                PermissionScope.Own, Now, Now.AddDays(7), "covering for Bob"),
            createdBy: Bob, Now);

        var granted = Grants.GetGrants(new PrincipalGrantQuery(Alice, [])).Should().ContainSingle().Subject;
        granted.Scope.Should().Be(PermissionScope.Own);
        granted.ValidUntil.Should().Be(Now.AddDays(7));
        granted.Reason.Should().Be("covering for Bob");
        saved.CreatedBy.Should().Be(Bob);
    }

    [Fact]
    public async Task Re_adding_the_same_grant_updates_its_window_instead_of_duplicating_it()
    {
        PermissionGrant Grant(DateTimeOffset until) => new(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own, ValidUntil: until);
        await Grants.AddScopedGrantAsync(Grant(Now.AddDays(1)), null, Now);
        await Grants.AddScopedGrantAsync(Grant(Now.AddDays(9)), null, Now);

        (await Grants.GetScopedGrantsForHolderAsync(GrantHolderType.User, Alice.ToString()))
            .Should().ContainSingle().Which.Grant.ValidUntil.Should().Be(Now.AddDays(9));
    }

    [Fact]
    public async Task Several_scopes_of_one_permission_coexist_and_remove_by_id()
    {
        var own = await Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own), null, Now);
        await Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")), null, Now);

        Grants.GetGrants(new PrincipalGrantQuery(Alice, [])).Should().HaveCount(2);
        (await Grants.RemoveScopedGrantAsync(own.Id)).Should().BeTrue();
        (await Grants.RemoveScopedGrantAsync(own.Id)).Should().BeFalse();
        Grants.GetGrants(new PrincipalGrantQuery(Alice, [])).Should().ContainSingle()
            .Which.Scope.Should().Be(PermissionScope.Assigned("customer"));
    }

    [Fact]
    public async Task A_scoped_deny_a_scope_less_restriction_and_a_backwards_window_are_refused()
    {
        var scopedDeny = () => Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Deny, PermissionScope.Own), null, Now);
        var bareRestriction = () => Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Restrict), null, Now);
        var backwards = () => Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, ValidFrom: Now, ValidUntil: Now.AddDays(-1)), null, Now);

        await scopedDeny.Should().ThrowAsync<ArgumentException>();
        await bareRestriction.Should().ThrowAsync<ArgumentException>();
        await backwards.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_scoped_grant_makes_the_permission_effective_until_it_expires()
    {
        await Grants.AddScopedGrantAsync(new PermissionGrant(
            GrantHolderType.User, Alice.ToString(), "orders:approve", PermissionGrantType.Allow, ValidUntil: Now.AddDays(3)), null, Now);
        using var scope = _provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<PermissionResolver>();

        resolver.Resolve(new PrincipalGrantQuery(Alice, [])).Should().Contain("orders:approve");

        _clock.Value = Now.AddDays(4);
        resolver.Resolve(new PrincipalGrantQuery(Alice, [])).Should().NotContain("orders:approve");
    }

    [Fact]
    public async Task Assignments_are_stored_effective_dated_and_removable()
    {
        var store = _provider.GetRequiredService<EfAssignmentStore>();
        await store.AssignAsync(new Assignment(Alice, "Customer", Acme));
        await store.AssignAsync(new Assignment(Alice, "customer", Globex, ValidUntil: Now.AddDays(-1)));

        store.TargetsFor(Alice, "customer", Now).Should().BeEquivalentTo([Acme]);
        (await store.ListAsync(Alice)).Should().HaveCount(2);
        (await store.UnassignAsync(Alice, "CUSTOMER", Acme)).Should().BeTrue();
        store.TargetsFor(Alice, "customer", Now).Should().BeEmpty();
    }

    [Fact]
    public void The_assignment_store_replaces_the_in_memory_default()
        => _provider.GetRequiredService<IAssignmentStore>().Should().BeOfType<EfAssignmentStore>();

    // ── The scope as real SQL ──

    private async Task<int> VisibleOrdersAsync(Guid user, string permission = "orders:read")
    {
        _user.Id = user;
        using var scope = _provider.CreateScope();
        var enforcer = scope.ServiceProvider.GetRequiredService<IScopeEnforcer>();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDb>();
        return await enforcer.Apply(sales.Orders.AsNoTracking(), permission).CountAsync();
    }

    [Fact]
    public async Task Own_and_assigned_scopes_translate_to_sql()
    {
        await Grants.AddScopedGrantAsync(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own), null, Now);
        await Grants.AddScopedGrantAsync(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")), null, Now);
        await _provider.GetRequiredService<EfAssignmentStore>().AssignAsync(new Assignment(Bob, "customer", Acme));

        (await VisibleOrdersAsync(Alice)).Should().Be(1);
        (await VisibleOrdersAsync(Bob)).Should().Be(2);
    }

    [Fact]
    public async Task A_union_and_a_restriction_translate_to_sql_and_nothing_granted_returns_nothing()
    {
        await Grants.GrantToUserAsync(Bob, ["orders:read"]);
        await Grants.AddScopedGrantAsync(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Restrict, PermissionScope.Own), null, Now);

        (await VisibleOrdersAsync(Bob)).Should().Be(2, "company-wide narrowed to Bob's own");
        (await VisibleOrdersAsync(Alice)).Should().Be(0, "no grant");
        (await VisibleOrdersAsync(Bob, "orders:approve")).Should().Be(0);
    }

    [Fact]
    public async Task The_list_and_the_by_id_check_agree_in_the_database_too()
    {
        await Grants.AddScopedGrantAsync(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")), null, Now);
        await _provider.GetRequiredService<EfAssignmentStore>().AssignAsync(new Assignment(Bob, "customer", Globex));
        _user.Id = Bob;
        using var scope = _provider.CreateScope();
        var enforcer = scope.ServiceProvider.GetRequiredService<IScopeEnforcer>();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDb>();

        var listed = await enforcer.Apply(sales.Orders.AsNoTracking(), "orders:read").Select(o => o.Id).OrderBy(i => i).ToListAsync();
        var byId = (await sales.Orders.AsNoTracking().ToListAsync()).Where(o => enforcer.IsInScope(o, "orders:read")).Select(o => o.Id).Order().ToList();

        listed.Should().NotBeEmpty().And.Equal(byId);
    }
}
