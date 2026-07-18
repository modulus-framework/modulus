using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the pure field security profile resolver: deny-by-default for sensitive
/// classifications, Public open, classification rules gate on a permission, per-field
/// overrides win over the classification, and read/write are configured independently
/// (blueprint §5.9, §11).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldSecurityProfileTests
{
    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Confidential, read: "conf:read")
        .Classification(FieldClassification.Restricted, read: "comp:read", write: "comp:write")
        .Field("Ssn", read: "ssn:read", write: "ssn:write"));

    [Fact]
    public void PublicField_IsOpen_ForReadAndWrite()
    {
        Profile.ReadRequirement("Name", FieldClassification.Public).Kind
            .Should().Be(FieldRequirementKind.Open);
        Profile.WriteRequirement("Name", FieldClassification.Public).Kind
            .Should().Be(FieldRequirementKind.Open);
    }

    [Fact]
    public void SensitiveField_WithNoConfiguredClearance_IsClosed_FailClosed()
    {
        // The empty profile configures nothing, so every non-public field is closed.
        FieldSecurityProfile.Empty.ReadRequirement("Margin", FieldClassification.Confidential).Kind
            .Should().Be(FieldRequirementKind.Closed);
        FieldSecurityProfile.Empty.WriteRequirement("Margin", FieldClassification.Restricted).Kind
            .Should().Be(FieldRequirementKind.Closed);
    }

    [Fact]
    public void ClassificationRule_GatesOnItsPermission()
    {
        var read = Profile.ReadRequirement("Margin", FieldClassification.Confidential);

        read.Kind.Should().Be(FieldRequirementKind.Permission);
        read.Permission.Should().Be("conf:read");
    }

    [Fact]
    public void ConfiguringReadOnly_LeavesWriteClosed()
    {
        // Confidential opened read but not write → writing stays fail-closed.
        Profile.WriteRequirement("Margin", FieldClassification.Confidential).Kind
            .Should().Be(FieldRequirementKind.Closed);
    }

    [Fact]
    public void ReadAndWrite_AreConfiguredIndependently()
    {
        Profile.ReadRequirement("Salary", FieldClassification.Restricted).Permission
            .Should().Be("comp:read");
        Profile.WriteRequirement("Salary", FieldClassification.Restricted).Permission
            .Should().Be("comp:write");
    }

    [Fact]
    public void FieldOverride_WinsOverClassificationRule()
    {
        // Ssn is Restricted (comp:read/comp:write by class) but the field override
        // demands its own narrower permission instead.
        Profile.ReadRequirement("Ssn", FieldClassification.Restricted).Permission
            .Should().Be("ssn:read");
        Profile.WriteRequirement("Ssn", FieldClassification.Restricted).Permission
            .Should().Be("ssn:write");
    }

    [Fact]
    public void PartialFieldOverride_FallsBackToClassification_ForTheOtherDirection()
    {
        var profile = FieldSecurityProfile.Define(p => p
            .Classification(FieldClassification.Restricted, read: "comp:read", write: "comp:write")
            .Field("Bonus", read: "bonus:read")); // override read only

        profile.ReadRequirement("Bonus", FieldClassification.Restricted).Permission
            .Should().Be("bonus:read");
        profile.WriteRequirement("Bonus", FieldClassification.Restricted).Permission
            .Should().Be("comp:write", "an unspecified override direction falls through to the class rule");
    }

    [Fact]
    public void BlankPermission_IsRejected()
    {
        var act = () => FieldSecurityProfile.Define(p =>
            p.Classification(FieldClassification.Confidential, read: "  "));

        act.Should().Throw<ArgumentException>();
    }
}
