using FluentAssertions;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>RULE-10 and RULE-11: a message's wire identity is its URN, not its .NET type name.</summary>
[Trait("Category", "Unit")]
public sealed class MessageUrnTests
{
    [MessageUrn("urn:orders:order-submitted:v1")]
    public sealed record OrderSubmitted(Guid OrderId);

    // The same URN as OrderSubmitted under a new .NET name: a rename must keep the identity of a released message.
    [MessageUrn("urn:orders:order-submitted:v1")]
    public sealed record OrderSubmittedRenamed(Guid OrderId);

    [MessageUrn("urn:orders:order-submitted:v2")]
    public sealed record OrderSubmittedV2(Guid OrderId, string Currency);

    public sealed record Unattributed(string Text);

    [Fact]
    public void An_attributed_type_is_identified_by_its_urn()
    {
        MessageUrn.Of(typeof(OrderSubmitted)).Should().Be("urn:orders:order-submitted:v1");
    }

    [Fact]
    public void An_unattributed_type_is_identified_by_a_derived_urn()
    {
        MessageUrn.Of(typeof(Unattributed)).Should().StartWith("urn:clr:").And.EndWith(nameof(Unattributed));
    }

    [Fact]
    public void A_renamed_type_keeps_the_urn_of_the_released_message()
    {
        // The wire name is the urn: the same urn resolves to the type that carries it, whatever its CLR name is.
        MessageUrn.Of(typeof(OrderSubmittedRenamed)).Should().Be(MessageUrn.Of(typeof(OrderSubmitted)));
    }

    [Fact]
    public void A_new_version_has_its_own_urn_so_both_versions_can_be_consumed()
    {
        MessageUrn.Of(typeof(OrderSubmittedV2)).Should().NotBe(MessageUrn.Of(typeof(OrderSubmitted)));
        var list = new MessageTypeAllowList(() => [typeof(OrderSubmitted), typeof(OrderSubmittedV2)]);

        list.Resolve("urn:orders:order-submitted:v1").Should().Be(typeof(OrderSubmitted));
        list.Resolve("urn:orders:order-submitted:v2").Should().Be(typeof(OrderSubmittedV2));
    }

    [Fact]
    public void The_assembly_qualified_name_still_resolves_for_messages_in_flight_during_a_deploy()
    {
        var list = new MessageTypeAllowList(() => [typeof(OrderSubmitted)]);

        list.Resolve(typeof(OrderSubmitted).AssemblyQualifiedName).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void Two_types_under_one_urn_are_refused_at_build_time()
    {
        var act = () => MessageUrn.EnsureUnique([typeof(OrderSubmitted), typeof(OrderSubmittedRenamed)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*urn:orders:order-submitted:v1*");
    }

    [Fact]
    public void Two_types_under_one_urn_are_not_resolved_at_all_when_discovered_later()
    {
        // Both types are in the allow-list through the registrations, so the URN is ambiguous and refused, not guessed.
        var list = new MessageTypeAllowList(() => [typeof(OrderSubmitted), typeof(OrderSubmittedRenamed)]);

        list.Resolve("urn:orders:order-submitted:v1").Should().BeNull();
        list.Resolve(typeof(OrderSubmitted).AssemblyQualifiedName).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void A_sealed_message_wrapper_is_allowed_so_an_encrypted_body_is_not_skipped()
    {
        // A sealed message travels as EncryptedPayload; the bus's allow-list must resolve that wrapper (FR-22, D6).
        var builder = new MessagingBusBuilder().AddConsumer<NeverUsedConsumer, OrderSubmitted>("never");
        var urn = MessageUrn.Of(typeof(EncryptedPayload));

        builder.MessageTypes.Resolve(urn).Should().Be(typeof(EncryptedPayload));
    }

    public sealed class NeverUsedConsumer : IConsumer<OrderSubmitted>
    {
        public Task Consume(ConsumeContext<OrderSubmitted> context) => Task.CompletedTask;
    }

    [Fact]
    public void A_standalone_transport_resolves_a_urn_to_its_type()
    {
        // Without a bus there is no registry, so the loaded type carrying the urn is found. Regression: the conformance
        // suite runs standalone transports, which failed when URNs were not resolved.
        MessageTypeAllowList.ResolveWireType("urn:orders:order-submitted:v1", allowList: null).Should().Be(typeof(OrderSubmitted));
        MessageTypeAllowList.ResolveWireType(MessageUrn.Of(typeof(Unattributed)), allowList: null).Should().Be(typeof(Unattributed));
    }

    [Theory]
    [InlineData("orders:order-submitted")]
    [InlineData("urn:has space")]
    [InlineData("")]
    public void A_malformed_urn_is_refused(string urn)
    {
        var act = () => new MessageUrnAttribute(urn);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_urn_longer_than_the_limit_is_refused()
    {
        var act = () => new MessageUrnAttribute("urn:" + new string('a', MessageUrnAttribute.MaxLength));

        act.Should().Throw<ArgumentException>();
    }
}
