using FluentAssertions;
using Modulus.Authorization.Grants;
using Modulus.Authorization;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// P1b-GATE (revocation latency, the part that needs no owner target): the permission resolver keeps no decision cache, so a
/// revoked grant stops applying on the very next check made by the same resolver instance.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RevocationLatencyTests
{
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-00000000c0de");

    private static PermissionRegistry Registry()
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");
        registry.Freeze();
        return registry;
    }

    [Fact]
    public void A_revoked_user_grant_stops_applying_on_the_next_check_with_the_same_resolver()
    {
        var store = new InMemoryPermissionGrantStore().GrantToUser(User, "ops:read");
        var resolver = new PermissionResolver(store, Registry());
        resolver.Resolve(new PrincipalGrantQuery(User, [])).Should().Contain("ops:read");

        store.RevokeFromUser(User, "ops:read");

        resolver.Resolve(new PrincipalGrantQuery(User, [])).Should().NotContain("ops:read");
    }

    [Fact]
    public void A_revoked_role_grant_stops_applying_to_its_holders_on_the_next_check()
    {
        var store = new InMemoryPermissionGrantStore().GrantToRole("Buyer", "ops:read");
        var resolver = new PermissionResolver(store, Registry());
        resolver.Resolve(new PrincipalGrantQuery(User, ["Buyer"])).Should().Contain("ops:read");

        store.RevokeFromRole("Buyer", "ops:read");

        resolver.Resolve(new PrincipalGrantQuery(User, ["Buyer"])).Should().NotContain("ops:read");
    }

    [Fact]
    public void A_grant_added_after_a_check_applies_on_the_next_check()
    {
        var store = new InMemoryPermissionGrantStore();
        var resolver = new PermissionResolver(store, Registry());
        resolver.Resolve(new PrincipalGrantQuery(User, [])).Should().NotContain("ops:read");

        store.GrantToUser(User, "ops:read");

        resolver.Resolve(new PrincipalGrantQuery(User, [])).Should().Contain("ops:read");
    }
}
