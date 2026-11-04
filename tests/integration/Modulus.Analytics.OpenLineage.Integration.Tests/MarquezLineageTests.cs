namespace Modulus.Analytics.OpenLineage.Integration.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Analytics.Testing;
using Xunit;

/// <summary>Backfill run events sent through the emitter are stored by a real Marquez and can be read back through its API.</summary>
[Collection(MarquezCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MarquezLineageTests(MarquezFixture marquez)
{
    private const string JobName = "analytics.backfill.orders_v1";

    private static ServiceProvider BuildEmitter(Uri lineageUrl)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:OpenLineage:Enabled"] = "true",
            ["Analytics:OpenLineage:Url"] = lineageUrl.ToString(),
            ["Analytics:OpenLineage:Namespace"] = "modulus",
            ["Analytics:OpenLineage:WarehouseNamespace"] = "modulus-warehouse",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAnalyticsSink>(new InMemoryAnalyticsSink("clickhouse"));
        services.AddModulusOpenLineage(config);
        return services.BuildServiceProvider();
    }

    private static AnalyticsRunLineage Event(Guid runId, AnalyticsLineageState state, string? error = null)
        => new(runId, state, "orders", 1, DateTimeOffset.UtcNow, error);

    private async Task<JsonElement> GetAsync(string path)
    {
        using var http = new HttpClient { BaseAddress = marquez.ApiBase };
        using var response = await http.GetAsync(path);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, $"Marquez answers {path}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_completed_backfill_is_stored_by_marquez_with_its_source_and_warehouse_table()
    {
        await using var provider = BuildEmitter(new Uri(marquez.ApiBase, "/api/v1/lineage"));
        var emitter = provider.GetRequiredService<IAnalyticsLineageEmitter>();
        var runId = Guid.NewGuid();

        await emitter.EmitAsync(Event(runId, AnalyticsLineageState.Start), CancellationToken.None);
        await emitter.EmitAsync(Event(runId, AnalyticsLineageState.Complete), CancellationToken.None);

        var run = await GetAsync($"/api/v1/jobs/runs/{runId:D}");
        run.GetProperty("state").GetString().Should().Be("COMPLETED");

        var job = await GetAsync($"/api/v1/namespaces/modulus/jobs/{JobName}");
        job.GetProperty("name").GetString().Should().Be(JobName);

        var lineage = await GetAsync($"/api/v1/lineage?nodeId={Uri.EscapeDataString($"job:modulus:{JobName}")}&depth=2");
        var nodes = lineage.GetProperty("graph").EnumerateArray().Select(n => n.GetProperty("id").GetString()).ToArray();
        nodes.Should().Contain("dataset:modulus:orders", "the operational source is an input");
        nodes.Should().Contain("dataset:modulus-warehouse:clickhouse/orders_v1", "the warehouse table is an output");
    }

    [Fact]
    public async Task A_failed_backfill_is_stored_as_failed_with_its_error()
    {
        await using var provider = BuildEmitter(new Uri(marquez.ApiBase, "/api/v1/lineage"));
        var emitter = provider.GetRequiredService<IAnalyticsLineageEmitter>();
        var runId = Guid.NewGuid();

        await emitter.EmitAsync(Event(runId, AnalyticsLineageState.Start), CancellationToken.None);
        await emitter.EmitAsync(Event(runId, AnalyticsLineageState.Fail, "the source is gone"), CancellationToken.None);

        var run = await GetAsync($"/api/v1/jobs/runs/{runId:D}");
        run.GetProperty("state").GetString().Should().Be("FAILED");
        run.GetProperty("facets").GetProperty("errorMessage").GetProperty("message").GetString().Should().Be("the source is gone");
    }
}
