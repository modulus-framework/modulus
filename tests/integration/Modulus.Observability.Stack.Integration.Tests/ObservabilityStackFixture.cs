namespace Modulus.Observability.Stack.Integration.Tests;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

[CollectionDefinition(Name)]
public sealed class ObservabilityStackCollection : ICollectionFixture<ObservabilityStackFixture>
{
    public const string Name = "Observability stack";
}

/// <summary>
/// The stack from deploy/observability, started from the same configuration files the compose file mounts: the OpenTelemetry
/// Collector in front of Tempo, Loki, Prometheus and Grafana. A test that uses it therefore checks the shipped configuration.
/// </summary>
public sealed class ObservabilityStackFixture : IAsyncLifetime
{
    private const string CollectorImage = "otel/opentelemetry-collector-contrib:0.116.1";
    private const string TempoImage = "grafana/tempo:2.6.1";
    private const string LokiImage = "grafana/loki:3.3.2";
    private const string PrometheusImage = "prom/prometheus:v2.55.1";
    private const string GrafanaImage = "grafana/grafana:11.4.0";

    public const string GrafanaPassword = "modulus-dev-only";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly string _stack = Path.Combine(RepositoryRoot(), "deploy", "observability");
    private readonly IContainer _tempo;
    private readonly IContainer _loki;
    private readonly IContainer _prometheus;
    private readonly IContainer _collector;
    private readonly IContainer _grafana;

    public ObservabilityStackFixture()
    {
        _tempo = new ContainerBuilder(TempoImage)
            .WithNetwork(_network).WithNetworkAliases("tempo")
            .WithCommand("-config.file=/etc/tempo.yaml")
            .WithBindMount(Path.Combine(_stack, "tempo", "tempo.yaml"), "/etc/tempo.yaml")
            .WithPortBinding(3200, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3200).ForPath("/ready")))
            .Build();

        _loki = new ContainerBuilder(LokiImage)
            .WithNetwork(_network).WithNetworkAliases("loki")
            .WithCommand("-config.file=/etc/loki/loki.yaml")
            .WithBindMount(Path.Combine(_stack, "loki", "loki.yaml"), "/etc/loki/loki.yaml")
            .WithPortBinding(3100, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3100).ForPath("/ready")))
            .Build();

        _prometheus = new ContainerBuilder(PrometheusImage)
            .WithNetwork(_network).WithNetworkAliases("prometheus")
            .WithCommand("--config.file=/etc/prometheus/prometheus.yml", "--web.enable-remote-write-receiver")
            .WithBindMount(Path.Combine(_stack, "prometheus", "prometheus.yml"), "/etc/prometheus/prometheus.yml")
            .WithBindMount(Path.Combine(_stack, "prometheus", "rules"), "/etc/prometheus/rules")
            .WithPortBinding(9090, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready")))
            .Build();

        _collector = new ContainerBuilder(CollectorImage)
            .WithNetwork(_network).WithNetworkAliases("collector")
            .WithCommand("--config=/etc/otelcol/otel-collector.yaml")
            .WithBindMount(Path.Combine(_stack, "collector", "otel-collector.yaml"), "/etc/otelcol/otel-collector.yaml")
            .WithPortBinding(4317, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4317))
            .Build();

        _grafana = new ContainerBuilder(GrafanaImage)
            .WithNetwork(_network).WithNetworkAliases("grafana")
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", GrafanaPassword)
            .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
            .WithBindMount(Path.Combine(_stack, "grafana", "provisioning"), "/etc/grafana/provisioning")
            .WithBindMount(Path.Combine(_stack, "grafana", "dashboards"), "/var/lib/grafana/dashboards")
            .WithPortBinding(3000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3000).ForPath("/api/health")))
            .Build();
    }

    public Uri Tempo => new UriBuilder("http", "localhost", _tempo.GetMappedPublicPort(3200)).Uri;

    public Uri Loki => new UriBuilder("http", "localhost", _loki.GetMappedPublicPort(3100)).Uri;

    public Uri Prometheus => new UriBuilder("http", "localhost", _prometheus.GetMappedPublicPort(9090)).Uri;

    public Uri Grafana => new UriBuilder("http", "localhost", _grafana.GetMappedPublicPort(3000)).Uri;

    /// <summary>The collector's OTLP gRPC endpoint on the host, what an application sets as <c>OpenTelemetry:Otlp:Endpoint</c>.</summary>
    public Uri CollectorGrpc => new UriBuilder("http", "localhost", _collector.GetMappedPublicPort(4317)).Uri;

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        // Storage first, then the collector, which exports to them, then Grafana, which reads them.
        await Task.WhenAll(_tempo.StartAsync(), _loki.StartAsync(), _prometheus.StartAsync());
        await _collector.StartAsync();
        await _grafana.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _grafana.DisposeAsync();
        await _collector.DisposeAsync();
        await _prometheus.DisposeAsync();
        await _loki.DisposeAsync();
        await _tempo.DisposeAsync();
        await _network.DisposeAsync();
    }

    /// <summary>The repository root: the folder above the one that holds <c>modulus.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "modulus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("modulus.slnx was not found above the test output.");
    }
}
