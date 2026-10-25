using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A classified member that masking cannot write would reach the caller unmasked, so the type is refused.</summary>
[Trait("Category", "Unit")]
public sealed class UnmaskableFieldTests
{
    private sealed class ReadOnlyCost
    {
        public Guid Id { get; set; }

        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; } = 5m;
    }

    private sealed class SettableCost
    {
        public Guid Id { get; set; }

        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; set; } = 5m;
    }

    private sealed class EmptyRegistry : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType) => FieldSecurityProfile.Empty;
    }

    [Fact]
    public void A_classified_get_only_member_refuses_the_type_and_names_it()
    {
        var authorizer = new FieldAuthorizer(Substitute.For<ICurrentUser>(), new EmptyRegistry());

        var act = () => authorizer.Redact(new ReadOnlyCost());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cost*no setter*");
    }

    [Fact]
    public void A_classified_settable_member_is_masked_as_before()
    {
        var authorizer = new FieldAuthorizer(Substitute.For<ICurrentUser>(), new EmptyRegistry());

        var projection = authorizer.Redact(new SettableCost { Cost = 9m });

        projection.Cost.Should().Be(0m, "the caller holds no permission for the restricted field");
    }
}
