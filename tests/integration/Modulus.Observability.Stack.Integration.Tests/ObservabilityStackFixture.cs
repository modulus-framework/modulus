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
/// Collector in front of Jaeger (traces), VictoriaLogs (logs) and Prometheus (metrics). Every component is Apache-2.0. A test that
/// uses it therefore checks the shipped configuration.
/// </summary>
public sealed class ObservabilityStackFixture : IAsyncLifetime
{
    private const string CollectorImage = "otel/opentelemetry-collector-contrib:0.116.1";
    private const string JaegerImage = "jaegertracing/jaeger:2.11.0";
    private const string VictoriaLogsImage = "victoriametrics/victoria-logs:v1.20.0-victorialogs";
    private const string PrometheusImage = "prom/prometheus:v2.55.1";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly string _stack = Path.Combine(RepositoryRoot(), "deploy", "observability");
    private readonly IContainer _jaeger;
    private readonly IContainer _victoriaLogs;
    private readonly IContainer _prometheus;
    private readonly IContainer _collector;

    public ObservabilityStackFixture()
    {
        _jaeger = new ContainerBuilder(JaegerImage)
            .WithNetwork(_network).WithNetworkAliases("jaeger")
            .WithCommand("--config=/etc/jaeger/config.yaml")
            .WithBindMount(Path.Combine(_stack, "jaeger", "config.yaml"), "/etc/jaeger/config.yaml")
            .WithPortBinding(16686, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready"))
            .Build();

        _victoriaLogs = new ContainerBuilder(VictoriaLogsImage)
            .WithNetwork(_network).WithNetworkAliases("victorialogs")
            .WithCommand("-storageDataPath=/vlogs", "-retentionPeriod=15d")
            .WithPortBinding(9428, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9428).ForPath("/health")))
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
            // The collector image has no shell, so a port check that runs a command inside it never succeeds. Wait for its
            // own readiness message instead.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready. Begin running and processing data."))
            .Build();
    }

    /// <summary>The tail of the container logs, for a failure message.</summary>
    public async Task<string> DiagnosticsAsync()
    {
        var collector = await _collector.GetLogsAsync();
        var jaeger = await _jaeger.GetLogsAsync();
        return "--- collector ---\n" + Tail(collector.Stdout + collector.Stderr) + "\n--- jaeger ---\n" + Tail(jaeger.Stdout + jaeger.Stderr);
    }

    private static string Tail(string text) => text.Length <= 3000 ? text : text[^3000..];

    /// <summary>The Jaeger query API and UI on the host.</summary>
    public Uri Jaeger => new UriBuilder("http", "localhost", _jaeger.GetMappedPublicPort(16686)).Uri;

    /// <summary>VictoriaLogs' query API on the host.</summary>
    public Uri VictoriaLogs => new UriBuilder("http", "localhost", _victoriaLogs.GetMappedPublicPort(9428)).Uri;

    public Uri Prometheus => new UriBuilder("http", "localhost", _prometheus.GetMappedPublicPort(9090)).Uri;

    /// <summary>The collector's OTLP gRPC endpoint on the host, what an application sets as <c>OpenTelemetry:Otlp:Endpoint</c>.</summary>
    public Uri CollectorGrpc => new UriBuilder("http", "localhost", _collector.GetMappedPublicPort(4317)).Uri;

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        // Storage first, then the collector, which exports to them.
        await Task.WhenAll(_jaeger.StartAsync(), _victoriaLogs.StartAsync(), _prometheus.StartAsync());
        await _collector.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _collector.DisposeAsync();
        await _prometheus.DisposeAsync();
        await _victoriaLogs.DisposeAsync();
        await _jaeger.DisposeAsync();
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
