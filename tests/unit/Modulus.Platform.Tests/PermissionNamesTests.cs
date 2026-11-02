using FluentAssertions;
using Modulus.Authorization;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class PermissionNamesTests
{
    [Theory]
    [InlineData("catalog:products:manage", true)]
    [InlineData("Org.Groups.View", true)]
    [InlineData("Admin", false)]
    [InlineData("Org.Groups", false)]
    public void Policy_names_that_are_permissions_are_recognised(string name, bool expected)
        => PermissionNames.IsPermissionPolicy(name).Should().Be(expected);

    [Theory]
    [InlineData("org:*", true)]
    [InlineData("Org.Groups.*", true)]
    [InlineData("Org.Groups.View", false)]
    public void Wildcards_use_either_separator(string name, bool expected)
        => PermissionNames.IsWildcard(name).Should().Be(expected);

    [Theory]
    [InlineData("org:groups:view", "org")]
    [InlineData("Org.Groups.View", "Org")]
    public void Module_is_the_part_before_the_first_separator(string name, string module)
    {
        PermissionNames.ModuleOf(name).Should().Be(module);
        PermissionNames.InModule(name, module).Should().BeTrue();
        PermissionNames.InModule(name, module + "x").Should().BeFalse();
    }
}
