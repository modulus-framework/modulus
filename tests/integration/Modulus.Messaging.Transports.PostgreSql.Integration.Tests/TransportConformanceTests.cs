using FluentAssertions;
using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class TransportConformanceTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task The_PostgreSQL_transport_passes_the_transport_conformance_suite()
    {
        var report = await TransportConformance.RunAsync(() => new PostgreSqlTransport(database.Options()));

        report.EnsurePassed();
        report.Passed.Should().HaveCount(6);
    }
}
