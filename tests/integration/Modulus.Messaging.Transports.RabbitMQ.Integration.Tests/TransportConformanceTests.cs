using FluentAssertions;
using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class TransportConformanceTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task The_RabbitMQ_transport_passes_the_transport_conformance_suite()
    {
        var report = await TransportConformance.RunAsync(() => new RabbitMqTransport(broker.Options()));

        report.EnsurePassed();
        report.Passed.Should().HaveCount(6);
    }
}
