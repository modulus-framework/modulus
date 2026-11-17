namespace Modulus.Observability.Stack.Integration.Tests;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    public async Task Traces_logs_and_metrics_sent_through_the_collector_reach_jaeger_victorialogs_and_prometheus()
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
        // Without a host, OpenTelemetry builds its providers only when something resolves them. The tracer provider registers the
        // Modulus activity sources; until it exists, StartActivity returns null and no span is produced.
        _ = provider.GetRequiredService<global::OpenTelemetry.Trace.TracerProvider>();
        // The same applies to the meter provider: without it, the counter below is never exported.
        _ = provider.GetRequiredService<global::OpenTelemetry.Metrics.MeterProvider>();

        // One request: a span, a counter, and a log record inside the span, so the log carries the span's trace id.
        var logger = provider.GetRequiredService<ILogger<ObservabilityStackTests>>();
        string? traceId;
        using (var activity = ModulusActivitySources.Mediator.StartActivity(marker))
        {
            traceId = activity?.TraceId.ToString();
            ModulusMeters.Mediator.CreateCounter<long>("obs.smoke.requests").Add(1, new KeyValuePair<string, object?>("marker", marker));
            logger.LogInformation("{Marker} handled by the smoke test", marker);
        }

        // Disposing the providers flushes the batched spans, log records and the last metric export.
        await provider.DisposeAsync();

        using var http = new HttpClient();
        var traceFound = await WaitForAsync(async () => await TraceExists(http, marker));
        traceFound.Should().BeTrue($"the span reached Jaeger\n{await stack.DiagnosticsAsync()}");

        // The log body is the message template, so the marker is not in the line. The record is found by its trace id instead.
        traceId.Should().NotBeNullOrEmpty("the smoke test ran inside a span");
        (await WaitForAsync(async () => await LogHasTraceIdAsync(http, traceId!))).Should().BeTrue(
            "the log record reached VictoriaLogs with its trace id");

        (await WaitForAsync(async () => await MetricExists(http))).Should().BeTrue("the counter reached Prometheus");
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
        // Jaeger's query API: traces of one service and operation (the span name).
        var json = await GetJsonAsync(http, new Uri(stack.Jaeger, $"/api/traces?service={ServiceName}&operation={Uri.EscapeDataString(marker)}&limit=5"));
        return json is not null && json.Value.TryGetProperty("data", out var data) && data.GetArrayLength() > 0;
    }

    private async Task<bool> LogHasTraceIdAsync(HttpClient http, string traceId)
    {
        // VictoriaLogs' LogsQL query. The response is one JSON object per matching line.
        var query = Uri.EscapeDataString($"trace_id:\"{traceId}\"");
        using var response = await http.GetAsync(new Uri(stack.VictoriaLogs, $"/select/logsql/query?query={query}"));
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        return (await response.Content.ReadAsStringAsync()).Trim().Length > 0;
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
