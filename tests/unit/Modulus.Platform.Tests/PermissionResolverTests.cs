using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class PermissionResolverTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IPermissionRegistry Registry(params (string name, string[] requires)[] perms)
    {
        var registry = new PermissionRegistry();
        foreach (var (name, requires) in perms)
            registry.Add(name, name, requires);
        registry.Freeze();
        return registry;
    }

    private static PermissionResolver Resolver(
        InMemoryPermissionGrantStore store, IPermissionRegistry registry)
        => new(store, registry);

    [Fact]
    public void NoGrants_ResolvesToEmpty_FailClosed()
    {
        var resolver = Resolver(new InMemoryPermissionGrantStore(),
            Registry(("sales:order:read", [])));

        var effective = resolver.Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().BeEmpty();
    }

    [Fact]
    public void AnonymousPrincipal_ResolvesToEmpty()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "sales:order:read");

        var effective = Resolver(store, Registry(("sales:order:read", [])))
            .Resolve(PrincipalGrantQuery.Anonymous);

        effective.Should().BeEmpty();
    }

    [Fact]
    public void RoleAllow_MakesPermissionEffective()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "sales:order:read");

        var effective = Resolver(store, Registry(("sales:order:read", [])))
            .Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().Contain("sales:order:read");
    }

    [Fact]
    public void DirectUserAllow_MakesPermissionEffective_EvenWithNoRoles()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToUser(User, "reports:view");

        var effective = Resolver(store, Registry(("reports:view", [])))
            .Resolve(new PrincipalGrantQuery(User, []));

        effective.Should().Contain("reports:view");
    }

    [Fact]
    public void RoleDeny_OverridesRoleAllow()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "sales:order:delete")
            .DenyToRole("clerk", "sales:order:delete");

        var effective = Resolver(store, Registry(("sales:order:delete", [])))
            .Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().NotContain("sales:order:delete");
    }

    [Fact]
    public void UserDeny_OverridesRoleAllow()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "sales:order:delete")
            .DenyToUser(User, "sales:order:delete");

        var effective = Resolver(store, Registry(("sales:order:delete", [])))
            .Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().NotContain("sales:order:delete");
    }

    [Fact]
    public void Allow_ConfersRequiredPermissions_Transitively()
    {
        // approve requires update; update requires read.
        var registry = Registry(
            ("sales:order:read", []),
            ("sales:order:update", ["sales:order:read"]),
            ("sales:order:approve", ["sales:order:update"]));
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("manager", "sales:order:approve");

        var effective = Resolver(store, registry)
            .Resolve(new PrincipalGrantQuery(User, ["manager"]));

        effective.Should().Contain(
            ["sales:order:approve", "sales:order:update", "sales:order:read"]);
    }

    [Fact]
    public void Deny_IsAppliedAfterImplicationClosure()
    {
        // approve implies read, but read is explicitly denied → read is not effective,
        // approve remains.
        var registry = Registry(
            ("sales:order:read", []),
            ("sales:order:approve", ["sales:order:read"]));
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("manager", "sales:order:approve")
            .DenyToRole("manager", "sales:order:read");

        var effective = Resolver(store, registry)
            .Resolve(new PrincipalGrantQuery(User, ["manager"]));

        effective.Should().Contain("sales:order:approve");
        effective.Should().NotContain("sales:order:read");
    }

    [Fact]
    public void WildcardAllow_ExpandsToRegisteredPermissionsUnderPrefix()
    {
        var registry = Registry(
            ("sales:order:read", []),
            ("sales:order:write", []),
            ("sales:invoice:read", []));
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("sales-admin", "sales:order:*");

        var effective = Resolver(store, registry)
            .Resolve(new PrincipalGrantQuery(User, ["sales-admin"]));

        effective.Should().Contain(["sales:order:read", "sales:order:write"]);
        effective.Should().NotContain("sales:invoice:read");
    }

    [Fact]
    public void WildcardDeny_RemovesMatchingPermissions()
    {
        var registry = Registry(
            ("sales:order:read", []),
            ("sales:order:write", []));
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("sales-admin", "sales:order:*")
            .DenyToRole("sales-admin", "sales:order:write");

        var effective = Resolver(store, registry)
            .Resolve(new PrincipalGrantQuery(User, ["sales-admin"]));

        effective.Should().Contain("sales:order:read");
        effective.Should().NotContain("sales:order:write");
    }

    [Fact]
    public void UnknownWildcardPrefix_ExpandsToNothing_FailClosed()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("ghost", "nonexistent:module:*");

        var effective = Resolver(store, Registry(("sales:order:read", [])))
            .Resolve(new PrincipalGrantQuery(User, ["ghost"]));

        effective.Should().BeEmpty();
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("Clerk", "Sales:Order:Read");

        var effective = Resolver(store, Registry(("sales:order:read", [])))
            .Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().Contain("sales:order:read");
    }

    [Fact]
    public void ExplicitNamedGrant_ResolvesEvenIfNotInRegistry()
    {
        // A grant may reference a permission whose module is not loaded; it still
        // resolves as a literal capability (only implication/wildcards need the catalog).
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "future:feature:use");

        var effective = Resolver(store, Registry(("sales:order:read", [])))
            .Resolve(new PrincipalGrantQuery(User, ["clerk"]));

        effective.Should().Contain("future:feature:use");
    }
}
