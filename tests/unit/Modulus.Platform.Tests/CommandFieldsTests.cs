using FluentAssertions;
using Modulus.Authorization.Fields;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The fields a command sets are read from its parameters: an optional parameter left at <c>null</c> changes nothing, and a required
/// nullable one is set even when it is cleared. <c>Id</c> is never a field.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CommandFieldsTests
{
    private sealed record UpdateCommand(Guid Id, string Name, IReadOnlyDictionary<string, string?>? ExtraProperties = null);

    private sealed record ScopedUpdate(Guid Id, string Name, Guid? OrgUnitId);

    private sealed record CreateWithOptionalScope(string Name, Guid? OrgUnitId = null);

    [Fact]
    public void An_optional_extra_properties_left_null_is_not_a_field_the_command_sets()
    {
        CommandFields.SetBy(new UpdateCommand(Guid.NewGuid(), "Widget")).Should().Equal("Name");
    }

    [Fact]
    public void A_supplied_extra_properties_is_a_field_the_command_sets()
    {
        CommandFields.SetBy(new UpdateCommand(Guid.NewGuid(), "Widget", new Dictionary<string, string?> { ["Colour"] = "red" }))
            .Should().Equal("Name", "ExtraProperties");
    }

    [Fact]
    public void A_required_nullable_field_cleared_to_null_is_still_a_field_the_command_sets()
    {
        CommandFields.SetBy(new ScopedUpdate(Guid.NewGuid(), "Widget", OrgUnitId: null))
            .Should().Equal(["Name", "OrgUnitId"], "a cleared scope field must be checked, not skipped");
    }

    [Fact]
    public void An_optional_nullable_parameter_left_at_its_default_is_not_a_field()
    {
        CommandFields.SetBy(new CreateWithOptionalScope("Widget")).Should().Equal("Name");
    }

    [Fact]
    public void The_id_is_never_listed()
    {
        CommandFields.SetBy(new ScopedUpdate(Guid.NewGuid(), "Widget", Guid.NewGuid()))
            .Should().NotContain("Id");
    }
}
