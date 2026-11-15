using Npgsql;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>One PostgreSQL 17 database shared by the tests of a class (Podman or Docker).</summary>
public sealed class PostgreSqlBroker : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("modulus_messaging")
        .WithUsername("modulus")
        .WithPassword("modulus-test-secret")
        // Port-based readiness: the default waits run pg_isready inside the container, which rootless Podman cannot exec here.
        // The server accepts TCP only after its initialisation restart, so an open port means ready.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(5432))
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A data source for the tests' own SQL (inspection and seeding rows). Shared, disposed with the fixture.</summary>
    public NpgsqlDataSource DataSource() => _dataSource ?? throw new InvalidOperationException("The database is not started.");

    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
        await WaitForQueryAsync(_dataSource, TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// The port can accept connections before the server is ready (rootless Podman forwards it at once and resets early
    /// connections), so wait until a real query succeeds.
    /// </summary>
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

    /// <summary>Stops the database container, as an outage would.</summary>
    public Task StopDatabaseAsync() => _container.StopAsync();

    /// <summary>Starts the database container again after <see cref="StopDatabaseAsync"/>.</summary>
    public async Task StartDatabaseAsync()
    {
        await _container.StartAsync();

        // Connections pooled before the stop were ended by the server; drop them and wait for a working connection.
        NpgsqlConnection.ClearAllPools();
        await WaitForQueryAsync(_dataSource!, TimeSpan.FromSeconds(60));
    }

    public PostgreSqlTransportOptions Options(TimeSpan? stopTimeout = null, TimeSpan? leaseDuration = null) => new()
    {
        ConnectionString = ConnectionString,
        StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(10),
        LeaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5),
        PollInterval = TimeSpan.FromMilliseconds(50),
    };
}
