namespace Modulus.Search.Integration.Tests;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

/// <summary>One OpenSearch server, with the security plugin off for the test only (no credentials, a single node).</summary>
public sealed class OpenSearchFixture : IAsyncLifetime
{
    private const int Port = 9200;
    private IContainer _container = null!;

    /// <summary>The server address, for example <c>http://localhost:32768/</c>.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("opensearchproject/opensearch:2.11.1")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
            .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath("/_cluster/health")))
            .Build();
        await _container.StartAsync();
        Endpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/";
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>One Apache Solr server (standalone mode) with the core <see cref="Core"/> precreated, as an application's operator would.</summary>
public sealed class SolrFixture : IAsyncLifetime
{
    public const string Core = "shop";
    private const int Port = 8983;
    private IContainer _container = null!;

    /// <summary>The server address, for example <c>http://localhost:32769/</c>.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("solr:9.6")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithCommand("solr-precreate", Core)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath($"/solr/{Core}/admin/ping")))
            .Build();
        await _container.StartAsync();
        Endpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/";
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class OpenSearchCollection : ICollectionFixture<OpenSearchFixture>
{
    public const string Name = "OpenSearch";
}

[CollectionDefinition(Name)]
public sealed class SolrCollection : ICollectionFixture<SolrFixture>
{
    public const string Name = "Solr";
}
