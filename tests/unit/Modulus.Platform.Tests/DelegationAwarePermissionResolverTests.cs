using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the decorator that makes delegation take effect at the capability layer: a
/// principal's resolved set is their direct authority unioned with the permissions
/// currently delegated to them, so <c>HasPermission</c> honours delegated authority with
/// no change to the checker (blueprint §5.13).
/// </summary>
[Trait("Category", "Unit")]
public sealed class DelegationAwarePermissionResolverTests
{
    private static readonly Guid Deputy = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Manager = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IPermissionRegistry Registry(params string[] perms)
    {
        var registry = new PermissionRegistry();
        foreach (var p in perms)
            registry.Add(p, p, null);
        registry.Freeze();
        return registry;
    }

    private sealed class StubDelegations(params string[] permissions) : IDelegationResolver
    {
        public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId)
            => [.. permissions.Select(p => new DelegatedPermission(p, Manager, Guid.NewGuid()))];
    }

    [Fact]
    public void ResolvedSet_UnionsDirectAndDelegated()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToUser(Deputy, "orders:read");
        var direct = new PermissionResolver(grants, Registry("orders:read", "orders:approve"));
        var resolver = new DelegationAwarePermissionResolver(direct, new StubDelegations("orders:approve"));

        var effective = resolver.Resolve(new PrincipalGrantQuery(Deputy, []));

        effective.Should().BeEquivalentTo(["orders:read", "orders:approve"]);
    }

    [Fact]
    public void WithNoDelegations_ReturnsDirectAuthorityUnchanged()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToUser(Deputy, "orders:read");
        var direct = new PermissionResolver(grants, Registry("orders:read"));
        var resolver = new DelegationAwarePermissionResolver(direct, new StubDelegations());

        resolver.Resolve(new PrincipalGrantQuery(Deputy, []))
            .Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void AnonymousPrincipal_GetsNoDelegatedAuthority()
    {
        var direct = new PermissionResolver(new InMemoryPermissionGrantStore(), Registry("orders:approve"));
        var resolver = new DelegationAwarePermissionResolver(direct, new StubDelegations("orders:approve"));

        resolver.Resolve(PrincipalGrantQuery.Anonymous)
            .Should().BeEmpty("delegation is keyed to a user id; an anonymous principal has none");
    }
}
