using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Modulus.Messaging.Outbox.Npgsql.Integration.Tests;

/// <summary>One PostgreSQL 17 database shared by the tests of a class (Podman or Docker).</summary>
public sealed class NpgsqlBroker : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("modulus_outbox")
        .WithUsername("modulus")
        .WithPassword("modulus-test-secret")
        // Port-based readiness: the default waits run pg_isready inside the container, which rootless Podman cannot exec here.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(5432))
        .Build();

    private NpgsqlDataSource? _dataSource;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The shared data source the tests and the bus use.</summary>
    public NpgsqlDataSource DataSource() => _dataSource ?? throw new InvalidOperationException("The database is not started.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
        await WaitForQueryAsync(_dataSource, TimeSpan.FromSeconds(60));
    }

    private static async Task WaitForQueryAsync(NpgsqlDataSource dataSource, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync();
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
