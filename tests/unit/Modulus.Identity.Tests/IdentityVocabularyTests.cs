using FluentAssertions;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The principal kinds and suspension reasons are open vocabularies: the framework names the common ones and normalises any
/// application-defined name the same way, so an application adds its own kinds without a framework change.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityVocabularyTests
{
    [Theory]
    [InlineData("Employee", "employee")]
    [InlineData("  contractor  ", "contractor")]
    [InlineData("partner-user", "partner-user")]
    public void A_principal_kind_is_normalised_to_lower_case(string input, string expected)
        => PrincipalKinds.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("9lives")]
    [InlineData("has space")]
    [InlineData("under_score")]
    public void A_principal_kind_that_is_not_a_plain_name_is_refused(string input)
        => FluentActions.Invoking(() => PrincipalKinds.Normalize(input)).Should().Throw<ArgumentException>();

    [Fact]
    public void The_framework_names_only_a_person_and_a_service_account()
    {
        PrincipalKinds.Person.Should().Be("person");
        PrincipalKinds.ServiceAccount.Should().Be("service-account");
    }

    [Theory]
    [InlineData(SuspensionReasons.AdministrativeHold, true)]
    [InlineData(SuspensionReasons.Other, true)]
    [InlineData(SuspensionReasons.SecurityReview, false)]
    [InlineData(SuspensionReasons.PolicyViolation, false)]
    [InlineData(SuspensionReasons.Absence, false)]
    [InlineData("application-specific", false)]
    public void Only_the_open_reasons_require_a_note(string code, bool requiresNote)
        => SuspensionReasons.RequiresNote(code).Should().Be(requiresNote);
}
