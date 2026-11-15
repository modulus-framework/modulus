using Modulus.Messaging.Testing;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class PostgreSqlTlsTests(PostgreSqlTlsBroker broker) : IClassFixture<PostgreSqlTlsBroker>
{
    [Fact]
    public async Task The_session_is_encrypted_and_the_transport_passes_the_conformance_suite_over_TLS()
    {
        await using (var connection = new NpgsqlConnection(broker.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection);
            Assert.Equal(true, await command.ExecuteScalarAsync());
        }

        var options = new PostgreSqlTransportOptions { ConnectionString = broker.ConnectionString };
        var report = await TransportConformance.RunAsync(() => new PostgreSqlTransport(options));

        report.EnsurePassed();
    }

    [Fact]
    public async Task A_server_certificate_nobody_trusts_is_refused()
    {
        await using var transport = new PostgreSqlTransport(new PostgreSqlTransportOptions { ConnectionString = broker.UntrustedConnectionString });

        var error = await Assert.ThrowsAnyAsync<Exception>(() => transport.Start(["tls-refused"], (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.True(TlsAssert.IsCertificateFailure(error), error.ToString());
    }
}
