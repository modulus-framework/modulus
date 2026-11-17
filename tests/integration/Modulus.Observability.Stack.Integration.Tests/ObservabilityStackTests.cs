namespace Modulus.Observability.Stack.Integration.Tests;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core;
using Modulus.OpenTelemetry;
using Xunit;

[Trait("Category", "Integration")]
[Collection(ObservabilityStackCollection.Name)]
public sealed class ObservabilityStackTests(ObservabilityStackFixture stack)
{
    private const string ServiceName = "obs-smoke";

    /// <summary>A minimal host environment: the setup reads only the application name.</summary>
    private sealed class SmokeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = ServiceName;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task Traces_logs_and_metrics_sent_through_the_collector_reach_tempo_loki_and_prometheus_and_grafana_reads_them()
    {
        var marker = "obs-smoke-" + Guid.NewGuid().ToString("N")[..10];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:ServiceName"] = ServiceName,
            ["OpenTelemetry:Otlp:Endpoint"] = stack.CollectorGrpc.ToString(),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information));
        services.AddModulusOpenTelemetry(configuration, new SmokeEnvironment());
        var provider = services.BuildServiceProvider();

        // One request: a span, a counter, and a log record inside the span, so the log carries the span's trace id.
        var logger = provider.GetRequiredService<ILogger<ObservabilityStackTests>>();
        using (var activity = ModulusActivitySources.Mediator.StartActivity(marker))
        {
            ModulusMeters.Mediator.CreateCounter<long>("obs.smoke.requests").Add(1, new KeyValuePair<string, object?>("marker", marker));
            logger.LogInformation("{Marker} handled by the smoke test", marker);
        }

        // Disposing the providers flushes the batched spans, log records and the last metric export.
        await provider.DisposeAsync();

        using var http = new HttpClient();
        (await WaitForAsync(async () => await TraceExists(http, marker))).Should().BeTrue("the span reached Tempo");
        (await WaitForAsync(async () => await LogExists(http, marker))).Should().BeTrue("the log record reached Loki");
        (await WaitForAsync(async () => await MetricExists(http))).Should().BeTrue("the counter reached Prometheus");

        // Grafana provisioned the three data sources the stack's configuration describes.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(stack.Grafana, "/api/datasources"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{ObservabilityStackFixture.GrafanaPassword}")));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var types = json.RootElement.EnumerateArray().Select(d => d.GetProperty("type").GetString()).ToArray();
        types.Should().Contain(["prometheus", "loki", "tempo"]);
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return false;
    }

    private async Task<bool> TraceExists(HttpClient http, string marker)
    {
        var query = Uri.EscapeDataString($"{{ resource.service.name = \"{ServiceName}\" && name = \"{marker}\" }}");
        var json = await GetJsonAsync(http, new Uri(stack.Tempo, $"/api/search?q={query}&limit=5"));
        return json is not null && json.Value.TryGetProperty("traces", out var traces) && traces.GetArrayLength() > 0;
    }

    private async Task<bool> LogExists(HttpClient http, string marker)
    {
        var query = Uri.EscapeDataString($"{{service_name=\"{ServiceName}\"}} |= \"{marker}\"");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
        var start = now - TimeSpan.FromMinutes(10).Ticks * 100L;
        var json = await GetJsonAsync(http, new Uri(stack.Loki, $"/loki/api/v1/query_range?query={query}&start={start}&limit=10"));
        return json is not null
            && json.Value.GetProperty("data").GetProperty("result").GetArrayLength() > 0;
    }

    private async Task<bool> MetricExists(HttpClient http)
    {
        var json = await GetJsonAsync(http, new Uri(stack.Prometheus, "/api/v1/query?query=obs_smoke_requests_total"));
        return json is not null
            && json.Value.GetProperty("data").GetProperty("result").GetArrayLength() > 0;
    }

    private static async Task<JsonElement?> GetJsonAsync(HttpClient http, Uri uri)
    {
        try
        {
            using var response = await http.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
