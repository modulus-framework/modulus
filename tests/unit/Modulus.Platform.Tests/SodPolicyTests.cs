using FluentAssertions;
using Modulus.Authorization.Governance;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the segregation-of-duties policy as an analyzable standing control (blueprint
/// §5.6, §13): a violation is holding two or more mutually-exclusive permissions; holding
/// at most one is compliant; the empty policy never fires.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SodPolicyTests
{
    private static readonly SodPolicy MakerChecker = new(
    [
        new SodConstraint("payments-maker-checker",
            ["payments:create", "payments:approve"],
            "Whoever raises a payment must not also approve it (four-eyes)."),
        new SodConstraint("vendor-and-invoice",
            ["vendor:create", "invoice:approve"]),
    ]);

    private static IReadOnlySet<string> Held(params string[] permissions)
        => new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void HoldingBothSidesOfAConstraint_IsAViolation()
    {
        var violations = MakerChecker.Evaluate(Held("payments:create", "payments:approve"));

        violations.Should().ContainSingle()
            .Which.Constraint.Name.Should().Be("payments-maker-checker");
        violations.Single().HeldPermissions
            .Should().BeEquivalentTo(["payments:create", "payments:approve"]);
    }

    [Fact]
    public void HoldingOnlyOneSide_IsCompliant()
    {
        MakerChecker.Evaluate(Held("payments:create", "invoice:approve"))
            .Should().BeEmpty("one permission from each of two different constraints is fine");
    }

    [Fact]
    public void MultipleConstraints_AreAllReported()
    {
        var violations = MakerChecker.Evaluate(
            Held("payments:create", "payments:approve", "vendor:create", "invoice:approve"));

        violations.Select(v => v.Constraint.Name)
            .Should().BeEquivalentTo(["payments-maker-checker", "vendor-and-invoice"]);
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        MakerChecker.Evaluate(Held("PAYMENTS:CREATE", "Payments:Approve"))
            .Should().ContainSingle();
    }

    [Fact]
    public void EmptyPolicy_NeverViolates()
    {
        SodPolicy.Empty.Evaluate(Held("payments:create", "payments:approve"))
            .Should().BeEmpty();
    }

    [Fact]
    public void Constraints_AreExposed_ForGovernanceReview()
    {
        MakerChecker.Constraints.Should().HaveCount(2);
    }
}
