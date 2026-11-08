using FluentAssertions;
using Modulus.Authorization;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>After <c>Freeze</c> the registry takes no new or changed permission; declaring one again unchanged is harmless.</summary>
[Trait("Category", "Unit")]
public sealed class PermissionRegistryFreezeTests
{
    private static PermissionRegistry Frozen()
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");
        registry.Add("ops:wipe", "wipe", null, PermissionSensitivity.Critical);
        registry.Freeze();
        return registry;
    }

    [Fact]
    public void Declaring_a_permission_again_unchanged_after_freeze_is_a_no_op()
    {
        var registry = Frozen();

        registry.Add("ops:read", "read");
        registry.Add("ops:wipe", "wipe, again", null, PermissionSensitivity.Critical);

        registry.GetAll().Select(p => p.Permission).Should().BeEquivalentTo(["ops:read", "ops:wipe"]);
    }

    [Fact]
    public void A_new_permission_after_freeze_is_refused()
    {
        var registry = Frozen();

        var act = () => registry.Add("ops:new", "new");

        act.Should().Throw<InvalidOperationException>().WithMessage("*frozen*");
        registry.Exists("ops:new").Should().BeFalse();
    }

    [Fact]
    public void A_changed_permission_after_freeze_is_refused_and_keeps_its_meaning()
    {
        var registry = Frozen();

        var act = () => registry.Add("ops:wipe", "wipe", null, PermissionSensitivity.Normal);

        act.Should().Throw<InvalidOperationException>().WithMessage("*frozen*");
        registry.GetAll().Single(p => p.Permission == "ops:wipe").Sensitivity.Should().Be(PermissionSensitivity.Critical);
    }
}
