using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>"Same permission, different data": a grant carries a scope, scopes union, restrictions narrow, and lists and single-record checks agree.</summary>
[Trait("Category", "Unit")]
public sealed class PermissionScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Sales = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid SalesTeamA = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid Finance = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Acme = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid Globex = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");

    private sealed class Order : IHasOwner, IHasOrgUnit
    {
        public string Name { get; init; } = "";
        public Guid OwnerId { get; init; }
        public Guid OrgUnitId { get; init; }
        public Guid CustomerId { get; init; }
    }

    private static readonly Order[] Orders =
    [
        new() { Name = "alice-sales-acme", OwnerId = Alice, OrgUnitId = SalesTeamA, CustomerId = Acme },
        new() { Name = "bob-sales-globex", OwnerId = Bob, OrgUnitId = Sales, CustomerId = Globex },
        new() { Name = "bob-finance-acme", OwnerId = Bob, OrgUnitId = Finance, CustomerId = Acme },
    ];

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private sealed class User(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => id is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class DataScope(bool unrestricted = false, params Guid[] units) : ICurrentDataScope
    {
        public bool IsUnrestricted => unrestricted;
        public IReadOnlyCollection<Guid> OrgUnitIds => units;
    }

    private sealed class Source(Guid? user, params string[] roles) : IPrincipalGrantQuerySource
    {
        public PrincipalGrantQuery Current => user is null ? PrincipalGrantQuery.Anonymous : new PrincipalGrantQuery(user, roles);
    }

    private sealed class Harness
    {
        public Clock Time { get; } = new(Now);
        public InMemoryPermissionGrantStore Grants { get; } = new();
        public InMemoryAssignmentStore Assignments { get; } = new();
        public InMemoryOrgHierarchy Hierarchy { get; } = new InMemoryOrgHierarchy().AddUnit(Sales).AddUnit(SalesTeamA, Sales).AddUnit(Finance);
        public InMemoryDelegationStore Delegations { get; } = new();
        public IPermissionRegistry Registry { get; }

        public Harness()
        {
            var registry = new PermissionRegistry();
            registry.Add("orders:read", "Read orders.");
            registry.Add("orders:edit", "Edit orders.", ["orders:read"]);
            registry.Add("orders:approve", "Approve orders.");
            registry.Add("billing:read", "Read invoices.");
            registry.Freeze();
            Registry = registry;
        }

        public IScopeEnforcer As(Guid? user, bool unrestricted = false, Guid[]? units = null, bool delegations = false, bool declareMap = true, params string[] roles)
        {
            var services = new ServiceProviderStub(delegations ? Delegations : null);
            var resolver = new PermissionScopeResolver(new Source(user, roles), Grants, Registry, services, Time);
            var subject = new ScopeSubject(new User(user), Hierarchy, Assignments, new DataScope(unrestricted, units ?? []), Time);
            return new ScopeEnforcer(resolver, new ScopeMapRegistry(declareMap ? [BuildMap()] : []), subject);
        }

        public ResolvedScope Scope(Guid? user, string permission, bool delegations = false, params string[] roles)
            => As(user, delegations: delegations, roles: roles).Describe(permission);
    }

    private sealed class ServiceProviderStub(IDelegationStore? delegations) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IDelegationStore) ? delegations : null;
    }

    private static string[] Names(IEnumerable<Order> orders) => [.. orders.Select(o => o.Name).Order()];

    // ── The value type ──

    [Theory]
    [InlineData("tenant", ScopeKind.Tenant, null)]
    [InlineData("own", ScopeKind.Own, null)]
    [InlineData("org", ScopeKind.OrgUnit, null)]
    [InlineData("ASSIGNED:customer", ScopeKind.Assigned, "customer")]
    public void A_scope_round_trips_through_its_storage_form(string text, ScopeKind kind, string? value)
    {
        PermissionScope.TryParse(text, out var scope).Should().BeTrue();

        scope.Kind.Should().Be(kind);
        scope.Value.Should().Be(value);
        PermissionScope.TryParse(scope.Format(), out var again).Should().BeTrue();
        again.Should().Be(scope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("everything")]
    [InlineData("own:me")]
    [InlineData("org:not-a-guid")]
    [InlineData("assigned")]
    [InlineData("assigned:")]
    public void Anything_else_is_refused(string text)
        => PermissionScope.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void A_named_org_unit_scope_keeps_its_id()
        => PermissionScope.OrgUnit(Sales).OrgUnitId.Should().Be(Sales);

    // ── Resolution ──

    [Fact]
    public void A_grant_without_a_scope_still_means_the_whole_company()
    {
        var h = new Harness();
        h.Grants.GrantToUser(Alice, "orders:read");

        h.Scope(Alice, "orders:read").IsTenantWide.Should().BeTrue();
    }

    [Fact]
    public void Nothing_granted_covers_nothing_and_so_does_an_unknown_permission_or_an_anonymous_caller()
    {
        var h = new Harness();
        h.Grants.GrantToUser(Alice, "orders:read");

        h.Scope(Alice, "orders:approve").IsNone.Should().BeTrue();
        h.Scope(Alice, "orders:made-up").IsNone.Should().BeTrue();
        h.Scope(null, "orders:read").IsNone.Should().BeTrue();
    }

    [Fact]
    public void Same_permission_different_data_for_two_people()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own));
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        h.Assignments.Assign(Bob, "customer", Acme);

        Names(h.As(Alice).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("alice-sales-acme");
        Names(h.As(Bob).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("alice-sales-acme", "bob-finance-acme");
    }

    [Fact]
    public void Scopes_from_several_grants_union()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.Role, "rep", "orders:read", PermissionGrantType.Allow, PermissionScope.Own));
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        h.Assignments.Assign(Bob, "customer", Globex);

        var enforcer = h.As(Bob, roles: "rep");

        Names(enforcer.Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("bob-finance-acme", "bob-sales-globex");
    }

    [Fact]
    public void A_named_org_unit_covers_its_descendants_and_nothing_else()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.OrgUnit(Sales)));

        Names(h.As(Alice).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("alice-sales-acme", "bob-sales-globex");
    }

    [Fact]
    public void My_org_scope_follows_the_callers_placement_and_the_bypass_lifts_it()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.MyOrgUnits));

        Names(h.As(Alice, units: [Finance]).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("bob-finance-acme");
        Names(h.As(Alice, unrestricted: true).Apply(Orders.AsQueryable(), "orders:read")).Should().HaveCount(3);
        Names(h.As(Alice, units: []).Apply(Orders.AsQueryable(), "orders:read")).Should().BeEmpty("an unplaced caller matches nothing");
    }

    [Fact]
    public void Without_org_scoping_configured_my_org_scope_matches_nothing_rather_than_everything()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.MyOrgUnits));
        var resolver = new PermissionScopeResolver(new Source(Alice), h.Grants, h.Registry, new ServiceProviderStub(null), h.Time);
        var subjects = new[]
        {
            new ScopeSubject(new User(Alice), h.Hierarchy, h.Assignments, null, h.Time),
            new ScopeSubject(new User(Alice), h.Hierarchy, h.Assignments, Modulus.Core.Null.NullCurrentDataScope.Instance, h.Time),
        };

        foreach (var subject in subjects)
            new ScopeEnforcer(resolver, new ScopeMapRegistry([BuildMap()]), subject)
                .Apply(Orders.AsQueryable(), "orders:read").Should().BeEmpty();
    }

    [Fact]
    public void A_restriction_narrows_a_wider_grant()
    {
        var h = new Harness();
        h.Grants.GrantToRole("manager", "orders:read");
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Restrict, PermissionScope.OrgUnit(Sales)));

        var enforcer = h.As(Alice, roles: "manager");

        Names(enforcer.Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("alice-sales-acme", "bob-sales-globex");
        enforcer.Describe("orders:read").IsTenantWide.Should().BeFalse();
    }

    [Fact]
    public void A_restriction_alone_grants_nothing()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Restrict, PermissionScope.Own));

        h.Scope(Alice, "orders:read").IsNone.Should().BeTrue();
    }

    [Fact]
    public void Own_narrowed_by_a_department_needs_both()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Own));
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Restrict, PermissionScope.OrgUnit(Finance)));

        Names(h.As(Bob).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("bob-finance-acme");
    }

    [Fact]
    public void An_explicit_deny_wins_over_every_scope()
    {
        var h = new Harness();
        h.Grants.GrantToRole("manager", "orders:read");
        h.Grants.DenyToUser(Alice, "orders:read");

        h.Scope(Alice, "orders:read", roles: "manager").IsNone.Should().BeTrue();
    }

    [Fact]
    public void A_grant_stops_applying_when_it_expires_with_no_administrator_action()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:approve", PermissionGrantType.Allow,
            PermissionScope.Tenant, ValidFrom: Now.AddDays(-1), ValidUntil: Now.AddDays(7), Reason: "cover for a colleague"));
        var enforcer = h.As(Alice);

        enforcer.Describe("orders:approve").IsTenantWide.Should().BeTrue();

        h.Time.Value = Now.AddDays(8);
        h.As(Alice).Describe("orders:approve").IsNone.Should().BeTrue();
    }

    [Fact]
    public void A_grant_does_not_apply_before_it_starts()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:approve", PermissionGrantType.Allow,
            ValidFrom: Now.AddDays(1)));

        h.Scope(Alice, "orders:approve").IsNone.Should().BeTrue();
    }

    [Fact]
    public void A_grant_carries_its_scope_to_the_permissions_it_implies()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:edit", PermissionGrantType.Allow, PermissionScope.Own));

        Names(h.As(Alice).Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("alice-sales-acme");
    }

    [Fact]
    public void A_wildcard_grant_covers_everything_under_its_prefix_at_its_scope()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:*", PermissionGrantType.Allow, PermissionScope.Own));

        h.Scope(Alice, "orders:approve").AnyOf.Should().ContainSingle();
        h.Scope(Alice, "billing:read").IsNone.Should().BeTrue();
    }

    [Fact]
    public void A_delegate_gets_the_delegators_scope_never_more()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:approve", PermissionGrantType.Allow, PermissionScope.OrgUnit(Finance)));
        h.Delegations.Delegate(Bob, [], Alice, ["orders:approve"], Now.AddDays(-1), Now.AddDays(5));

        Names(h.As(Alice, delegations: true).Apply(Orders.AsQueryable(), "orders:approve")).Should().Equal("bob-finance-acme");
        h.As(Alice).Describe("orders:approve").IsNone.Should().BeTrue("without the delegation resolver the permission is not held");
    }

    [Fact]
    public void An_expired_delegation_gives_nothing_and_neither_does_one_the_delegator_no_longer_backs()
    {
        var h = new Harness();
        h.Grants.GrantToUser(Bob, "orders:approve");
        h.Delegations.Delegate(Bob, [], Alice, ["orders:approve"], Now.AddDays(-5), Now.AddDays(-1));
        h.As(Alice, delegations: true).Describe("orders:approve").IsNone.Should().BeTrue();

        h.Delegations.Delegate(Bob, [], Alice, ["orders:approve"], Now.AddDays(-1), Now.AddDays(5));
        h.As(Alice, delegations: true).Describe("orders:approve").IsTenantWide.Should().BeTrue();

        h.Grants.RevokeFromUser(Bob, "orders:approve");
        h.As(Alice, delegations: true).Describe("orders:approve").IsNone.Should().BeTrue("the delegator lost it, so the delegation lapses");
    }

    [Fact]
    public void A_deny_on_the_delegate_wins_over_a_delegation()
    {
        var h = new Harness();
        h.Grants.GrantToUser(Bob, "orders:approve");
        h.Grants.DenyToUser(Alice, "orders:approve");
        h.Delegations.Delegate(Bob, [], Alice, ["orders:approve"], Now.AddDays(-1), Now.AddDays(5));

        h.As(Alice, delegations: true).Describe("orders:approve").IsNone.Should().BeTrue();
    }

    // ── Fail-closed ──

    [Fact]
    public void A_type_with_no_key_for_the_scope_matches_nothing()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        h.Assignments.Assign(Alice, "customer", Acme);

        // Without a declared map the markers give owner + org unit only: no key for "customer".
        h.As(Alice, declareMap: false).Apply(Orders.AsQueryable(), "orders:read").Should().BeEmpty();
        h.As(Alice, declareMap: false).IsInScope(Orders[0], "orders:read").Should().BeFalse();
    }

    [Fact]
    public void An_assignment_that_has_ended_no_longer_counts()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Alice.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        h.Assignments.Assign(new Assignment(Alice, "customer", Acme, ValidUntil: Now.AddDays(-1)));

        h.As(Alice).Describe("orders:read").IsNone.Should().BeFalse();
        h.Assignments.TargetsFor(Alice, "customer", Now).Should().BeEmpty();
    }

    // ── A declared map, and lists agree with by-id checks (TST-003) ──

    [Fact]
    public void Lists_and_single_record_checks_agree_for_every_scope_shape()
    {
        var scopes = new[]
        {
            PermissionScope.Own, PermissionScope.MyOrgUnits, PermissionScope.OrgUnit(Sales), PermissionScope.Assigned("customer"),
        };

        foreach (var scope in scopes)
        {
            var h = new Harness();
            h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, scope));
            h.Assignments.Assign(Bob, "customer", Acme);
            var maps = new ScopeMapRegistry([BuildMap()]);
            var resolver = new PermissionScopeResolver(new Source(Bob), h.Grants, h.Registry, new ServiceProviderStub(null), h.Time);
            var subject = new ScopeSubject(new User(Bob), h.Hierarchy, h.Assignments, new DataScope(false, Finance), h.Time);
            var enforcer = new ScopeEnforcer(resolver, maps, subject);

            var listed = Names(enforcer.Apply(Orders.AsQueryable(), "orders:read"));
            var byId = Names(Orders.Where(o => enforcer.IsInScope(o, "orders:read")));

            byId.Should().Equal(listed, $"scope {scope} must hide the same records in a list and by id");
        }
    }

    [Fact]
    public void A_declared_map_names_the_keys_and_supports_assignment_scopes()
    {
        var h = new Harness();
        h.Grants.Add(new PermissionGrant(GrantHolderType.User, Bob.ToString(), "orders:read", PermissionGrantType.Allow, PermissionScope.Assigned("customer")));
        h.Assignments.Assign(Bob, "customer", Globex);
        var resolver = new PermissionScopeResolver(new Source(Bob), h.Grants, h.Registry, new ServiceProviderStub(null), h.Time);
        var subject = new ScopeSubject(new User(Bob), h.Hierarchy, h.Assignments, new DataScope(), h.Time);
        var enforcer = new ScopeEnforcer(resolver, new ScopeMapRegistry([BuildMap()]), subject);

        Names(enforcer.Apply(Orders.AsQueryable(), "orders:read")).Should().Equal("bob-sales-globex");
        enforcer.IsInScope(Orders[1], "orders:read").Should().BeTrue();
        enforcer.IsInScope(Orders[0], "orders:read").Should().BeFalse();
    }

    private static ScopeMap<Order> BuildMap()
    {
        var builder = new ScopeMapBuilder<Order>();
        builder.Owner(o => o.OwnerId).OrgUnit(o => o.OrgUnitId).AssignedKey("customer", o => o.CustomerId);
        return builder.Build();
    }
}
