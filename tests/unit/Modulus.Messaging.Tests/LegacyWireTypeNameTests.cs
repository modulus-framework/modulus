using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>
/// Rolling upgrade across the URN change (MassTransit-style message name formatter): the choice belongs to each bus, so one
/// process can run a bus that sends URNs beside one that sends the .NET name. Receivers accept both forms.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LegacyWireTypeNameTests
{
    public sealed class WireNameProbeConsumer : IConsumer<OrderSubmitted>
    {
        public Task Consume(ConsumeContext<OrderSubmitted> context) => Task.CompletedTask;
    }

    private static MessagingBus BusWith(Action<MessagingBusBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<WireNameProbeConsumer, OrderSubmitted>("wire-name-probe");
            configure?.Invoke(b);
            b.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<MessagingBus>();
    }

    [Fact]
    public void A_bus_sends_the_urn_by_default()
    {
        var bus = BusWith();

        bus.EnvelopeForSend(new Uri("queue:wire-name-probe"), new OrderSubmitted(Guid.NewGuid())).WireType
            .Should().Be(MessageUrn.Of(typeof(OrderSubmitted)));
    }

    [Fact]
    public void Legacy_mode_sends_the_assembly_qualified_name_and_a_receiver_still_resolves_it()
    {
        var bus = BusWith(b => b.UseLegacyWireTypeNames());

        var wire = bus.EnvelopeForSend(new Uri("queue:wire-name-probe"), new OrderSubmitted(Guid.NewGuid())).WireType;

        wire.Should().Be(typeof(OrderSubmitted).AssemblyQualifiedName);
        var receiver = new MessageTypeAllowList(() => [typeof(OrderSubmitted)]);
        MessageTypeAllowList.ResolveWireType(wire, receiver).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void A_custom_formatter_chooses_the_name()
    {
        var bus = BusWith(b => b.UseMessageNameFormatter(type => "acme:" + type.Name));

        bus.EnvelopeForSend(new Uri("queue:wire-name-probe"), new OrderSubmitted(Guid.NewGuid())).WireType
            .Should().Be("acme:OrderSubmitted");
    }

    [Fact]
    public void Two_buses_in_one_process_can_send_different_names()
    {
        var urn = BusWith();
        var legacy = BusWith(b => b.UseLegacyWireTypeNames());
        var message = new OrderSubmitted(Guid.NewGuid());

        urn.EnvelopeForSend(new Uri("queue:wire-name-probe"), message).WireType.Should().Be(MessageUrn.Of(typeof(OrderSubmitted)));
        legacy.EnvelopeForSend(new Uri("queue:wire-name-probe"), message).WireType.Should().Be(typeof(OrderSubmitted).AssemblyQualifiedName);
    }

    [Fact]
    public void A_null_formatter_is_refused()
    {
        var builder = new MessagingBusBuilder();

        var act = () => builder.UseMessageNameFormatter(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
