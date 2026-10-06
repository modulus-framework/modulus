namespace Modulus.Events.Tests;

using FluentAssertions;
using Modulus.Events.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IntegrationEventNamingTests
{
    [Fact]
    public void GetName_ForKnownEvent_ReturnsAttributedName()
    {
        var name = IntegrationEventNaming.GetName(typeof(TestEvent));
        name.Should().Be("test.event.v1");
    }

    [Fact]
    public void GetName_ForEventWithoutAttribute_ReturnsFullName()
    {
        var name = IntegrationEventNaming.GetName(typeof(UnattributedEvent));
        name.Should().Be("Modulus.Events.Tests.IntegrationEventNamingTests+UnattributedEvent");
    }

    [Fact]
    public void GetName_ConsistentAcrossCalls_ForSameType()
    {
        var name1 = IntegrationEventNaming.GetName(typeof(TestEvent));
        var name2 = IntegrationEventNaming.GetName(typeof(TestEvent));
        name1.Should().Be(name2);
    }

    [IntegrationEventName("test.event.v1")]
    private sealed class TestEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType => "test.event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    private sealed class UnattributedEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType => IntegrationEventNaming.GetName(GetType());
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
}

[Trait("Category", "Unit")]
public sealed class DerivedEventNameTests
{
    private sealed class PaymentsArea;

    [IntegrationEvent<PaymentsArea>]
    private sealed record SubscriptionPurchased(Guid Id) : IntegrationEventBase;

    [IntegrationEvent<PaymentsArea>(Version = 2)]
    private sealed record SubscriptionPurchasedIntegrationEvent(Guid Id) : IntegrationEventBase;

    [Fact]
    public void Name_is_derived_from_the_types_with_no_string_literal()
    {
        IntegrationEventNaming.GetName(typeof(SubscriptionPurchased)).Should().Be("payments.subscription-purchased.v1");
        new SubscriptionPurchased(Guid.NewGuid()).EventType.Should().Be("payments.subscription-purchased.v1");
    }

    [Fact]
    public void Version_and_the_IntegrationEvent_suffix_are_handled()
        => IntegrationEventNaming.GetName(typeof(SubscriptionPurchasedIntegrationEvent))
            .Should().Be("payments.subscription-purchased.v2");

    [Theory]
    [InlineData("SubscriptionPurchased", "subscription-purchased")]
    [InlineData("HTTPServerError", "http-server-error")]
    [InlineData("Order2Placed", "order2-placed")]
    [InlineData("A", "a")]
    public void Kebab_converts_pascal_case(string input, string expected)
        => IntegrationEventNaming.Kebab(input).Should().Be(expected);

    [Theory]
    [InlineData("PaymentsModule", "ProductCreatedIntegrationEvent", "payments.product-created.v1")]
    [InlineData("CatalogArea", "ProductCreatedEvent", "catalog.product-created.v1")]
    [InlineData("Billing", "Event", "billing.event.v1")]
    public void Suffixes_are_dropped_only_when_something_remains(string module, string eventType, string expected)
        => IntegrationEventNaming.Derive(module, eventType).Should().Be(expected);

    [Fact]
    public void Version_must_be_positive()
        => ((Action)(() => _ = new IntegrationEventAttribute<PaymentsArea> { Version = 0 }))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void HasDeclaredName_covers_both_attributes_and_rejects_neither()
    {
        IntegrationEventNaming.HasDeclaredName(typeof(SubscriptionPurchased)).Should().BeTrue();
        IntegrationEventNaming.HasDeclaredName(typeof(IntegrationEventNamingTests)).Should().BeFalse();
    }
}
