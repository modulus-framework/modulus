using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// TEST-2 (behavioural): the query the database runs for a scope must return exactly the rows the per-record check accepts, for every
/// scope shape, and an export must reach no further than reading. The rows come from a real SQLite table, not an in-memory list.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScopeQueryAgreementTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Sales = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid SalesTeamA = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid Finance = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Acme = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid Globex = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ScopeDb _db;

    public ScopeQueryAgreementTests()
    {
        _connection.Open();
        _db = new ScopeDb(new DbContextOptionsBuilder<ScopeDb>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Orders.AddRange(
            new ScopeOrder { Name = "alice-sales-acme", OwnerId = Alice, OrgUnitId = SalesTeamA, CustomerId = Acme },
            new ScopeOrder { Name = "bob-sales-globex", OwnerId = Bob, OrgUnitId = Sales, CustomerId = Globex },
            new ScopeOrder { Name = "bob-finance-acme", OwnerId = Bob, OrgUnitId = Finance, CustomerId = Acme },
            new ScopeOrder { Name = "bob-finance-globex", OwnerId = Bob, OrgUnitId = Finance, CustomerId = Globex });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class ScopeDb(DbContextOptions<ScopeDb> options) : DbContext(options)
    {
        public DbSet<ScopeOrder> Orders => Set<ScopeOrder>();
    }

    public sealed class ScopeOrder : IHasOwner, IHasOrgUnit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public Guid OwnerId { get; init; }
        public Guid OrgUnitId { get; init; }
        public Guid CustomerId { get; init; }
    }

    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class NoDataScope : ICurrentDataScope
    {
        public bool IsUnrestricted => false;
        public IReadOnlyCollection<Guid> OrgUnitIds => [];
    }

    private sealed class Source(Guid user) : IPrincipalGrantQuerySource
    {
        public PrincipalGrantQuery Current => new(user, []);
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static readonly ScopeMap<ScopeOrder> s_map = Map();

    private static ScopeMap<ScopeOrder> Map()
    {
        var builder = new ScopeMapBuilder<ScopeOrder>();
        builder.Owner(o => o.OwnerId).OrgUnit(o => o.OrgUnitId).AssignedKey("customer", o => o.CustomerId);
        return builder.Build();
    }

    // The enforcer a caller gets: their grants, their placements and assignments, and the sample map.
    private (IScopeEnforcer Enforcer, InMemoryPermissionGrantStore Grants, InMemoryAssignmentStore Assignments) EnforcerFor(Guid user)
    {
        var registry = new PermissionRegistry();
        registry.Add("orders:read", "Read orders.");
        registry.Add("orders:export", "Export orders.");
        registry.Freeze();
        var grants = new InMemoryPermissionGrantStore();
        var assignments = new InMemoryAssignmentStore();
        var hierarchy = new InMemoryOrgHierarchy().AddUnit(Sales).AddUnit(SalesTeamA, Sales).AddUnit(Finance);
        var resolver = new PermissionScopeResolver(new Source(user), grants, registry, new NoServices(), TimeProvider.System);
        var subject = new ScopeSubject(new FixedUser(user), hierarchy, assignments, new NoDataScope(), TimeProvider.System);
        return (new ScopeEnforcer(resolver, new ScopeMapRegistry([s_map]), subject), grants, assignments);
    }

    private static readonly (string Name, Action<InMemoryPermissionGrantStore, InMemoryAssignmentStore> Grant)[] s_shapes =
    [
        ("own", (g, _) => g.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own))),
        ("an org unit and its subtree", (g, _) => g.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.OrgUnit(Sales)))),
        ("an assigned customer", (g, a) =>
        {
            g.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
            a.Assign(Bob, "customer", Acme);
        }),
        ("the whole company", (g, _) => g.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow))),
    ];

    [Fact]
    public void The_database_query_returns_exactly_the_rows_the_record_check_accepts_for_every_scope_shape()
    {
        foreach (var (name, grant) in s_shapes)
        {
            var (enforcer, grants, assignments) = EnforcerFor(Bob);
            grant(grants, assignments);

            var fromDatabase = enforcer.Apply(_db.Orders.AsNoTracking(), "orders:read").Select(o => o.Name).ToList();
            var perRecord = _db.Orders.AsNoTracking().ToList()
                .Where(o => enforcer.IsInScope(o, "orders:read")).Select(o => o.Name).ToList();

            fromDatabase.Should().BeEquivalentTo(perRecord, $"the database and the record check disagree for {name}");
        }
    }

    [Fact]
    public void An_export_reaches_no_further_than_reading_in_the_database()
    {
        var (enforcer, grants, assignments) = EnforcerFor(Bob);
        grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:export", PermissionGrantType.Allow, PermissionScope.Own));
        assignments.Assign(Bob, "customer", Acme);

        var read = enforcer.Apply(_db.Orders.AsNoTracking(), "orders:read").Select(o => o.Name).ToList();
        var exported = enforcer.ForExport(_db.Orders.AsNoTracking(), "orders:export", "orders:read").Select(o => o.Name).ToList();

        // Read reaches both Acme orders (the assigned customer); the export reaches only Bob's own. The export gets the overlap.
        exported.Should().Equal("bob-finance-acme");
        read.Should().Contain(exported, "an export never returns a row the caller cannot read");
    }
}
