using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqTlsTests(RabbitMqTlsBroker broker) : IClassFixture<RabbitMqTlsBroker>
{
    [Fact]
    public async Task The_transport_passes_the_conformance_suite_over_TLS_when_the_certificate_is_trusted()
    {
        var report = await TransportConformance.RunAsync(() => new RabbitMqTransport(broker.Options(trust: true)));

        report.EnsurePassed();
    }

    [Fact]
    public async Task A_broker_certificate_nobody_trusts_is_refused()
    {
        await using var transport = new RabbitMqTransport(broker.Options(trust: false));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => transport.Start(["tls-refused"], (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.True(TlsAssert.IsCertificateFailure(error), error.ToString());
    }
}
