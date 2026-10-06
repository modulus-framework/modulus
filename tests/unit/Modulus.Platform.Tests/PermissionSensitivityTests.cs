using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class PermissionSensitivityTests
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static PermissionRegistry Registry()
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");
        registry.Add("ops:wipe", "wipe", null, PermissionSensitivity.Critical);
        registry.Freeze();
        return registry;
    }

    [Fact]
    public void Wildcard_grant_does_not_confer_a_critical_permission()
    {
        var grants = new InMemoryPermissionGrantStore().GrantToUser(User, "ops:*");

        new PermissionResolver(grants, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().BeEquivalentTo(["ops:read"]);
    }

    [Fact]
    public void Naming_a_critical_permission_still_grants_it_and_a_wildcard_deny_removes_it()
    {
        var named = new InMemoryPermissionGrantStore().GrantToUser(User, "ops:wipe");
        new PermissionResolver(named, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().BeEquivalentTo(["ops:wipe"]);

        var denied = new InMemoryPermissionGrantStore().GrantToUser(User, "ops:wipe").DenyToUser(User, "ops:*");
        new PermissionResolver(denied, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().BeEmpty();
    }

    [Fact]
    public void Denials_carry_stable_reason_codes_and_scope_probe_fails_closed()
    {
        var policy = ResourcePolicy.Define(p => p.Allow("view", r => r.InScopeOf("ops:read")));
        ResourceRequest Request(Func<string, bool>? probe) => new(
            User, _ => true, _ => true, new ResourceAttributes(User, null, null), "view", probe);

        policy.Evaluate(Request(null)).Code.Should().Be(AccessReasonCodes.PolicyViolation);
        policy.Evaluate(Request(_ => false)).IsAllowed.Should().BeFalse();

        var allowed = policy.Evaluate(Request(perm => perm == "ops:read"));
        allowed.IsAllowed.Should().BeTrue();
        allowed.Code.Should().Be(AccessReasonCodes.Allowed);
    }
}
