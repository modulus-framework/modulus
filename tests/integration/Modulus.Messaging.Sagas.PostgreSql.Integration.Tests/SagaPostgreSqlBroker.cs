using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Modulus.Messaging.Sagas.PostgreSql.Integration.Tests;

/// <summary>One PostgreSQL 17 database shared by the tests of a class (Podman or Docker).</summary>
public sealed class SagaPostgreSqlBroker : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("modulus_sagas")
        .WithUsername("modulus")
        .WithPassword("modulus-test-secret")
        // Port-based readiness: the default waits run pg_isready inside the container, which rootless Podman cannot exec here.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(5432))
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The port can accept connections before the server is ready, so wait for a real query.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
                await using var connection = await dataSource.OpenConnectionAsync();
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
