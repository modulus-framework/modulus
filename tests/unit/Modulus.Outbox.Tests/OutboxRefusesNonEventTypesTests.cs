using FluentAssertions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Dispatchers;
using NSubstitute;
using Xunit;

namespace Modulus.Outbox.Tests;

/// <summary>
/// A row whose message type is not an integration event is refused before its payload is deserialized: the stored type name is data,
/// and building an object from an arbitrary type is not a step to take.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboxRefusesNonEventTypesTests
{
    [Fact]
    public async Task A_row_naming_a_type_that_is_not_an_integration_event_is_refused_without_deserializing()
    {
        var registry = Substitute.For<IIntegrationEventRegistry>();
        var serializer = Substitute.For<IMessageSerializer>();
        var bus = Substitute.For<IModuleBus>();
        var dispatcher = new InProcessOutboxDispatcher(bus, registry, serializer);
        var row = new OutboxMessage { MessageType = typeof(string).AssemblyQualifiedName!, Payload = "{}" };

        Func<Task> act = () => dispatcher.DispatchAsync(row, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration event*");
        serializer.DidNotReceiveWithAnyArgs().Deserialize(default!, default!);
    }
}
