namespace Modulus.Analytics.OpenLineage.Tests;

using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Analytics.Testing;
using Xunit;

/// <summary>A real HTTP endpoint on a random local port, recording each run event it receives.</summary>
internal sealed class LineageReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LineageReceiver(WebApplication app) => _app = app;

    public ConcurrentQueue<JsonDocument> Events { get; } = new();

    public ConcurrentQueue<string> Authorization { get; } = new();

    /// <summary>The status the endpoint answers with.</summary>
    public int Status { get; set; } = StatusCodes.Status200OK;

    public string Url => _app.Urls.First() + "/api/v1/lineage";

    public static async Task<LineageReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new LineageReceiver(app);
        app.MapPost("/api/v1/lineage", async (HttpContext context) =>
        {
            receiver.Authorization.Enqueue(context.Request.Headers.Authorization.ToString());
            receiver.Events.Enqueue(await JsonDocument.ParseAsync(context.Request.Body));
            context.Response.StatusCode = receiver.Status;
        });
        await app.StartAsync();
        return receiver;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed class OpenLineageEmitterTests
{
    private static readonly Guid RunId = Guid.Parse("5b0c7f2e-1d3a-4c8b-9e6f-7a2d4b1c0e9f");
    private static readonly DateTimeOffset At = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static ServiceProvider Build(string? url, bool enabled = true, string? apiKey = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:OpenLineage:Enabled"] = enabled ? "true" : "false",
            ["Analytics:OpenLineage:Url"] = url,
            ["Analytics:OpenLineage:Timeout"] = "00:00:05",
            ["Analytics:OpenLineage:ApiKey"] = apiKey,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(new InMemoryAnalyticsSink("clickhouse"));
        services.AddSingleton<IAnalyticsSink>(new InMemoryAnalyticsSink("parquet"));
        services.AddModulusOpenLineage(config);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AnalyticsRunLineage Event(AnalyticsLineageState state, string? error = null)
        => new(RunId, state, "orders", 1, At, error);

    [Fact]
    public async Task A_start_event_is_posted_as_an_openlineage_run_event_with_its_source_and_warehouse_tables()
    {
        await using var receiver = await LineageReceiver.StartAsync();
        await using var provider = Build(receiver.Url);

        await provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Start), CancellationToken.None);

        receiver.Events.TryDequeue(out var posted).Should().BeTrue("the event reached the endpoint");
        var root = posted!.RootElement;
        root.GetProperty("eventType").GetString().Should().Be("START");
        root.GetProperty("eventTime").GetDateTimeOffset().Should().Be(At);
        root.GetProperty("producer").GetString().Should().Be("https://github.com/modulus-framework/modulus");
        root.GetProperty("schemaURL").GetString().Should().Be("https://openlineage.io/spec/2-0-2/OpenLineage.json#/$defs/RunEvent");
        root.GetProperty("run").GetProperty("runId").GetString().Should().Be(RunId.ToString("D"));
        root.GetProperty("run").TryGetProperty("facets", out _).Should().BeFalse("a start event carries no error facet");
        root.GetProperty("job").GetProperty("name").GetString().Should().Be("analytics.backfill.orders_v1");
        root.GetProperty("job").GetProperty("namespace").GetString().Should().Be("modulus");
        root.GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Should().Equal("orders");
        root.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString())
            .Should().Equal("clickhouse/orders_v1", "parquet/orders_v1");
        root.GetProperty("outputs").EnumerateArray().Should().OnlyContain(o => o.GetProperty("namespace").GetString() == "modulus-warehouse");
    }

    [Fact]
    public async Task A_failure_carries_its_error_as_an_error_facet()
    {
        await using var receiver = await LineageReceiver.StartAsync();
        await using var provider = Build(receiver.Url);

        await provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Fail, "the source is gone"), CancellationToken.None);

        receiver.Events.TryDequeue(out var posted).Should().BeTrue();
        var root = posted!.RootElement;
        root.GetProperty("eventType").GetString().Should().Be("FAIL");
        var error = root.GetProperty("run").GetProperty("facets").GetProperty("errorMessage");
        error.GetProperty("message").GetString().Should().Be("the source is gone");
        error.GetProperty("_schemaURL").GetString().Should().Be("https://openlineage.io/spec/facets/1-0-0/ErrorMessageRunFacet.json");
        error.GetProperty("programmingLanguage").GetString().Should().Be("C#");
    }

    [Fact]
    public async Task The_api_key_is_sent_as_a_bearer_token()
    {
        await using var receiver = await LineageReceiver.StartAsync();
        await using var provider = Build(receiver.Url, apiKey: "lineage-token");

        await provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Start), CancellationToken.None);

        receiver.Authorization.TryDequeue(out var header).Should().BeTrue();
        header.Should().Be("Bearer lineage-token");
    }

    [Fact]
    public async Task An_endpoint_that_refuses_the_event_does_not_fail_the_emitter()
    {
        await using var receiver = await LineageReceiver.StartAsync();
        receiver.Status = StatusCodes.Status500InternalServerError;
        await using var provider = Build(receiver.Url);

        var emit = () => provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Complete), CancellationToken.None);

        await emit.Should().NotThrowAsync();
        receiver.Events.Should().ContainSingle("the event was sent, and refused");
    }

    [Fact]
    public async Task An_endpoint_that_is_down_does_not_fail_the_emitter()
    {
        await using var provider = Build("http://127.0.0.1:1/api/v1/lineage");

        var emit = () => provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Complete), CancellationToken.None);

        await emit.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_disabled_emitter_posts_nothing()
    {
        await using var receiver = await LineageReceiver.StartAsync();
        await using var provider = Build(receiver.Url, enabled: false);

        await provider.GetRequiredService<IAnalyticsLineageEmitter>().EmitAsync(Event(AnalyticsLineageState.Start), CancellationToken.None);

        receiver.Events.Should().BeEmpty();
    }

    [Fact]
    public void An_enabled_emitter_with_a_non_http_url_is_refused_at_startup()
    {
        using var provider = Build("ftp://lineage.example.org/api/v1/lineage");

        var read = () => provider.GetRequiredService<IOptions<OpenLineageOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*absolute http or https URL*");
    }

    [Fact]
    public void A_disabled_emitter_needs_no_url()
    {
        using var provider = Build(url: null, enabled: false);

        var settings = provider.GetRequiredService<IOptions<OpenLineageOptions>>().Value;

        settings.Enabled.Should().BeFalse();
        settings.Url.Should().BeNull();
    }
}
