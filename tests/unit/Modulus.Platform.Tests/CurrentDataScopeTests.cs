using FluentAssertions;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the <see cref="CurrentDataScope"/> edge bridge resolves a principal's
/// organizational scope from its identity and is fail-closed: no user / no placement
/// yields no units and no bypass, while the bypass grant lifts the restriction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CurrentDataScopeTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Region = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Branch = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Team = Guid.Parse("00000000-0000-0000-0000-000000000004");

    // region → branch → team
    private static OrgScopeResolver Resolver(InMemoryOrgPlacementStore placements)
        => new(
            new InMemoryOrgHierarchy()
                .AddUnit(Region)
                .AddUnit(Branch, Region)
                .AddUnit(Team, Branch),
            placements);

    [Fact]
    public void AnonymousPrincipal_HasNoUnitsAndIsNotUnrestricted_FailClosed()
    {
        var scope = new CurrentDataScope(
            new StubUser(userId: null), Resolver(new InMemoryOrgPlacementStore()));

        scope.IsUnrestricted.Should().BeFalse();
        scope.OrgUnitIds.Should().BeEmpty();
    }

    [Fact]
    public void PlacedPrincipal_ExposesTheResolvedScopeUnits()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Branch, OrgScopeMode.UnitAndDescendants);

        var scope = new CurrentDataScope(new StubUser(User), Resolver(placements));

        scope.IsUnrestricted.Should().BeFalse();
        scope.OrgUnitIds.Should().BeEquivalentTo([Branch, Team]);
    }

    [Fact]
    public void BypassPermission_MakesTheScopeUnrestricted()
    {
        var scope = new CurrentDataScope(
            new StubUser(User, CurrentDataScope.BypassPermission),
            Resolver(new InMemoryOrgPlacementStore()));

        scope.IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public void OrgUnitIds_IsResolvedAtMostOnce_PerInstance()
    {
        var placements = new InMemoryOrgPlacementStore()
            .Place(User, Team, OrgScopeMode.UnitOnly);
        var scope = new CurrentDataScope(new StubUser(User), Resolver(placements));

        var first = scope.OrgUnitIds;

        // Mutating placements after first resolution must not change this request's
        // (memoised) view — the scope is request-consistent.
        placements.Remove(User, Team);

        scope.OrgUnitIds.Should().BeSameAs(first);
        first.Should().BeEquivalentTo([Team]);
    }

    private sealed class StubUser(Guid? userId, params string[] permissions) : ICurrentUser
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Guid? UserId => userId;
        public string? UserName => userId?.ToString();
        public string? Email => null;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => [.. _permissions];
    }
}
