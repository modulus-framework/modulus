using FluentAssertions;
using Modulus.Authorization.Grants;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class InMemoryPermissionGrantStoreTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void GetGrants_CombinesRoleAndUserGrants()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "a")
            .GrantToUser(User, "b");

        var grants = store.GetGrants(new PrincipalGrantQuery(User, ["clerk"]));

        grants.Select(g => g.Permission).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void GetGrants_IgnoresRolesTheStoreDoesNotKnow()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "a");

        var grants = store.GetGrants(new PrincipalGrantQuery(User, ["auditor"]));

        grants.Should().BeEmpty();
    }

    [Fact]
    public void GetGrants_CarriesGrantType()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "a")
            .DenyToRole("clerk", "b");

        var grants = store.GetGrants(new PrincipalGrantQuery(null, ["clerk"]));

        grants.Single(g => g.Permission == "a").Type.Should().Be(PermissionGrantType.Allow);
        grants.Single(g => g.Permission == "b").Type.Should().Be(PermissionGrantType.Deny);
    }

    [Fact]
    public void Revoke_RemovesAGrant()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "a", "b");

        store.RevokeFromRole("clerk", "a");

        store.GetGrants(new PrincipalGrantQuery(null, ["clerk"]))
            .Select(g => g.Permission).Should().BeEquivalentTo("b");
    }

    [Fact]
    public void GrantIsDynamic_AddedAfterConstructionIsVisible()
    {
        var store = new InMemoryPermissionGrantStore();
        var query = new PrincipalGrantQuery(User, ["clerk"]);

        store.GetGrants(query).Should().BeEmpty();

        store.GrantToRole("clerk", "a");

        store.GetGrants(query).Select(g => g.Permission).Should().BeEquivalentTo("a");
    }

    [Fact]
    public void ReGrant_OverwritesPriorTypeForSamePermission()
    {
        var store = new InMemoryPermissionGrantStore()
            .DenyToRole("clerk", "a")
            .GrantToRole("clerk", "a");

        var grant = store.GetGrants(new PrincipalGrantQuery(null, ["clerk"])).Single();

        grant.Type.Should().Be(PermissionGrantType.Allow);
    }
}
