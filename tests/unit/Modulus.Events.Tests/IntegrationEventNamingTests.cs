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
public sealed class StructuredEventNameTests
{
    [IntegrationEventName("payments", "subscription-purchased")]
    private sealed record Purchased(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("payments", "subscription-purchased", version: 2)]
    private sealed record PurchasedV2(Guid Id) : IntegrationEventBase;

    [Fact]
    public void Name_is_declared_once_and_flows_to_EventType()
    {
        IntegrationEventNaming.GetName(typeof(Purchased)).Should().Be("payments.subscription-purchased.v1");
        new Purchased(Guid.NewGuid()).EventType.Should().Be("payments.subscription-purchased.v1");
        new PurchasedV2(Guid.NewGuid()).EventType.Should().Be("payments.subscription-purchased.v2");
    }

    [Theory]
    [InlineData("Payments", "x")]
    [InlineData("payments", "Sub Purchased")]
    [InlineData("pay.ments", "x")]
    [InlineData("", "x")]
    public void Malformed_parts_are_rejected(string module, string name)
        => ((Action)(() => _ = new IntegrationEventNameAttribute(module, name))).Should().Throw<ArgumentException>();

    [Fact]
    public void Version_must_be_positive()
        => ((Action)(() => _ = new IntegrationEventNameAttribute("a", "b", 0))).Should().Throw<ArgumentOutOfRangeException>();
}
