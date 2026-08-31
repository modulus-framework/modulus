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
