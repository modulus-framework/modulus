using FluentAssertions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.Outbox.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxSchemaVersionTests
{
    public sealed class SchemaArea;

    [IntegrationEvent<SchemaArea>(Version = 3)]
    public sealed record VersionedEvent(string Value) : IntegrationEventBase;

    [IntegrationEvent<SchemaArea>]
    public sealed record DefaultVersionEvent(string Value) : IntegrationEventBase;

    public sealed record LegacyNamedEvent(string Value) : IntegrationEventBase;

    private static OutboxMessage Row(IIntegrationEvent @event)
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<object>(), Arg.Any<Type>()).Returns("{}");
        return OutboxRowFactory.Create(@event, Guid.NewGuid(), "orders", null, serializer);
    }

    [Fact]
    public void A_row_carries_the_declared_contract_version()
        => Row(new VersionedEvent("x")).SchemaVersion.Should().Be(3);

    [Fact]
    public void An_event_without_a_version_argument_is_version_one()
        => Row(new DefaultVersionEvent("x")).SchemaVersion.Should().Be(1);

    [Fact]
    public void A_legacy_named_event_has_no_schema_version()
    {
        var legacy = new LegacyNamedEvent("x");

        Row(legacy).SchemaVersion.Should().BeNull("the legacy name attribute declares no version");
    }

    [Fact]
    public void GetVersion_reads_the_attribute_only()
    {
        IntegrationEventNaming.GetVersion(typeof(VersionedEvent)).Should().Be(3);
        IntegrationEventNaming.GetVersion(typeof(LegacyNamedEvent)).Should().BeNull();
        IntegrationEventNaming.GetVersion(typeof(string)).Should().BeNull();
    }
}
