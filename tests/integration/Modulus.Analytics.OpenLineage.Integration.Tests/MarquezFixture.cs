namespace Modulus.Analytics.OpenLineage.Integration.Tests;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

[CollectionDefinition(Name)]
public sealed class MarquezCollection : ICollectionFixture<MarquezFixture>
{
    public const string Name = "Marquez lineage";
}

/// <summary>Marquez (the reference OpenLineage backend) with the PostgreSQL it stores its metadata in, on a private network.</summary>
public sealed class MarquezFixture : IAsyncLifetime
{
    private const string MarquezImage = "marquezproject/marquez:0.50.0";
    private const string PostgresImage = "postgres:16-alpine";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly IContainer _postgres;
    private readonly IContainer _marquez;

    public MarquezFixture()
    {
        _postgres = new ContainerBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithEnvironment("POSTGRES_USER", "marquez")
            .WithEnvironment("POSTGRES_PASSWORD", "marquez")
            .WithEnvironment("POSTGRES_DB", "marquez")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

        _marquez = new ContainerBuilder(MarquezImage)
            .WithNetwork(_network)
            .WithPortBinding(5000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(5000).ForPath("/api/v1/namespaces")))
            .Build();
    }

    /// <summary>The base address of the Marquez API on the host, for example <c>http://localhost:49153</c>.</summary>
    public Uri ApiBase => new UriBuilder("http", "localhost", _marquez.GetMappedPublicPort(5000)).Uri;

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await _postgres.StartAsync();
        await _marquez.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _marquez.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}
