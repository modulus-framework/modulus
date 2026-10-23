using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// ADR-03 deny precedence, one matrix across the sources a person can get a permission from: a direct grant, a role grant, and a
/// delegation. A deny on the person or on a role wins over every allow, whichever source the allow came from.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DenyPrecedenceTests
{
    private const string Permission = "orders:approve";
    private const string Role = "Approver";
    private static readonly Guid s_deputy = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_manager = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Delegations(bool delegated) : IDelegationResolver
    {
        public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId)
            => delegated ? [new DelegatedPermission(Permission, s_manager, Guid.NewGuid())] : [];
    }

    private static IPermissionRegistry Registry()
    {
        var registry = new PermissionRegistry();
        registry.Add(Permission, Permission, null);
        registry.Freeze();
        return registry;
    }

    private static IReadOnlySet<string> Resolve(InMemoryPermissionGrantStore grants, bool delegated)
    {
        var direct = new PermissionResolver(grants, Registry());
        var resolver = new DelegationAwarePermissionResolver(direct, new Delegations(delegated));
        return resolver.Resolve(new PrincipalGrantQuery(s_deputy, [Role]));
    }

    // allowedByUser / allowedByRole: where the allow comes from; delegated: whether a delegation also allows it; denyOn: where the deny sits.
    [Theory]
    [InlineData(true, false, false, "user")]
    [InlineData(true, false, false, "role")]
    [InlineData(false, true, false, "user")]
    [InlineData(false, true, false, "role")]
    [InlineData(false, false, true, "user")]
    [InlineData(false, false, true, "role")]
    [InlineData(true, true, true, "user")]
    [InlineData(true, true, true, "role")]
    public void A_deny_on_the_person_or_the_role_wins_over_every_allow_source(
        bool allowedByUser, bool allowedByRole, bool delegated, string denyOn)
    {
        var grants = new InMemoryPermissionGrantStore();
        if (allowedByUser)
            grants.GrantToUser(s_deputy, Permission);
        if (allowedByRole)
            grants.GrantToRole(Role, Permission);
        if (denyOn == "user")
            grants.DenyToUser(s_deputy, Permission);
        else
            grants.DenyToRole(Role, Permission);

        Resolve(grants, delegated).Should().NotContain(Permission, "a deny stands over every allow");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Without_a_deny_any_allow_source_grants_the_permission(bool allowedByUser, bool delegated)
    {
        var grants = new InMemoryPermissionGrantStore();
        if (allowedByUser)
            grants.GrantToUser(s_deputy, Permission);

        Resolve(grants, delegated).Should().Contain(Permission, "the control: with no deny, an allow applies");
    }

    [Fact]
    public void A_wildcard_deny_on_the_role_removes_a_delegated_permission_too()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToRole(Role, "orders:*").DenyToRole(Role, "orders:*");

        Resolve(grants, delegated: true).Should().NotContain(Permission);
    }
}
