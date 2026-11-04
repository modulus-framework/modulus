namespace Modulus.Analytics.ClickHouse.Integration.Tests;

using global::ClickHouse.Driver;
using Microsoft.Extensions.Options;
using Testcontainers.ClickHouse;
using Xunit;

[CollectionDefinition(Name)]
public sealed class ClickHouseCollection : ICollectionFixture<ClickHouseFixture>
{
    public const string Name = "ClickHouse analytics";
}

public sealed class ClickHouseFixture : IAsyncLifetime
{
    private readonly ClickHouseContainer _container = new ClickHouseBuilder("clickhouse/clickhouse-server:25.8-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public ClickHouseClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new ClickHouseClient(ConnectionString);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return _container.DisposeAsync().AsTask();
    }

    /// <summary>A sink with its own options on the shared server. Each test uses its own table prefix so tests do not collide.</summary>
    public ClickHouseAnalyticsSink CreateSink(string prefix, Action<ClickHouseSinkOptions>? configure = null, string? connectionString = null)
    {
        var options = new ClickHouseSinkOptions { ConnectionString = connectionString ?? ConnectionString, TablePrefix = prefix };
        configure?.Invoke(options);
        return new ClickHouseAnalyticsSink(connectionString is null ? Client : new ClickHouseClient(connectionString), Options.Create(options));
    }
}
