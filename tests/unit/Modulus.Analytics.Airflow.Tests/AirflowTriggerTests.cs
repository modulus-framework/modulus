namespace Modulus.Analytics.Airflow.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>A real HTTP endpoint that plays the Airflow REST API on a random local port and records each request.</summary>
internal sealed class AirflowReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;

    private AirflowReceiver(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Path, string? Authorization, JsonElement Body)> Requests { get; } = new();

    /// <summary>The status the endpoint answers with.</summary>
    public int Status { get; set; } = StatusCodes.Status200OK;

    public Uri BaseUrl => new(_app.Urls.First());

    public static async Task<AirflowReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new AirflowReceiver(app);
        app.MapPost("/api/v1/dags/{dag}/dagRuns", async (HttpContext context, string dag) =>
        {
            var body = await JsonDocument.ParseAsync(context.Request.Body);
            receiver.Requests.Enqueue(($"/api/v1/dags/{dag}/dagRuns", context.Request.Headers.Authorization.ToString(), body.RootElement.Clone()));
            context.Response.StatusCode = receiver.Status;
        });
        await app.StartAsync();
        return receiver;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed class AirflowTriggerTests
{
    private static readonly Guid RunId = Guid.Parse("c3a9e8f1-2b4d-4e6a-9f0b-1d2c3e4f5a6b");
    private static readonly Guid Tenant = Guid.Parse("0f1e2d3c-4b5a-4968-8a7b-6c5d4e3f2a1b");

    private static ServiceProvider Build(Uri? baseUrl, bool enabled = true, string dagId = "modulus_analytics_transform", string? username = null, string? apiToken = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Airflow:Enabled"] = enabled ? "true" : "false",
            ["Analytics:Airflow:BaseUrl"] = baseUrl?.ToString(),
            ["Analytics:Airflow:DagId"] = dagId,
            ["Analytics:Airflow:Username"] = username,
            ["Analytics:Airflow:Password"] = username is null ? null : "airflow-secret",
            ["Analytics:Airflow:ApiToken"] = apiToken,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAirflow(config);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AnalyticsRunCompleted Completed() => new(RunId, "orders", 1, Tenant, DateTimeOffset.UtcNow, 42);

    [Fact]
    public async Task A_completed_run_starts_the_dag_once_for_that_run_with_its_dataset_and_company_in_the_configuration()
    {
        await using var airflow = await AirflowReceiver.StartAsync();
        await using var provider = Build(airflow.BaseUrl, username: "modulus");

        await provider.GetRequiredService<IAnalyticsRunObserver>().OnBackfillCompletedAsync(Completed(), CancellationToken.None);

        airflow.Requests.TryDequeue(out var request).Should().BeTrue();
        request.Path.Should().Be("/api/v1/dags/modulus_analytics_transform/dagRuns");
        request.Body.GetProperty("dag_run_id").GetString().Should().Be($"modulus-{RunId:N}");
        var conf = request.Body.GetProperty("conf");
        conf.GetProperty("dataset").GetString().Should().Be("orders");
        conf.GetProperty("version").GetInt32().Should().Be(1);
        conf.GetProperty("tenant_id").GetString().Should().Be(Tenant.ToString("D"));
        conf.GetProperty("rows_queued").GetInt64().Should().Be(42);
        conf.GetProperty("dbt_select").GetString().Should().Be("tag:modulus_analytics");
        request.Authorization.Should().Be("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("modulus:airflow-secret")));
        airflow.Requests.Should().BeEmpty("one request per run");
    }

    [Fact]
    public async Task A_run_Airflow_already_started_is_not_an_error()
    {
        await using var airflow = await AirflowReceiver.StartAsync();
        airflow.Status = StatusCodes.Status409Conflict;
        await using var provider = Build(airflow.BaseUrl);

        var trigger = () => provider.GetRequiredService<IAnalyticsRunObserver>().OnBackfillCompletedAsync(Completed(), CancellationToken.None);

        await trigger.Should().NotThrowAsync("a retried trigger for the same run is refused by Airflow, which means it already started");
    }

    [Fact]
    public async Task A_refused_start_is_reported_to_the_backfill_as_a_failure()
    {
        await using var airflow = await AirflowReceiver.StartAsync();
        airflow.Status = StatusCodes.Status500InternalServerError;
        await using var provider = Build(airflow.BaseUrl);

        var trigger = () => provider.GetRequiredService<IAnalyticsRunObserver>().OnBackfillCompletedAsync(Completed(), CancellationToken.None);

        await trigger.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_disabled_trigger_calls_nothing()
    {
        await using var airflow = await AirflowReceiver.StartAsync();
        await using var provider = Build(airflow.BaseUrl, enabled: false);

        await provider.GetRequiredService<IAnalyticsRunObserver>().OnBackfillCompletedAsync(Completed(), CancellationToken.None);

        airflow.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_api_token_is_sent_as_a_bearer_token_instead_of_basic_credentials()
    {
        await using var airflow = await AirflowReceiver.StartAsync();
        await using var provider = Build(airflow.BaseUrl, username: "modulus", apiToken: "airflow-token");

        await provider.GetRequiredService<IAnalyticsRunObserver>().OnBackfillCompletedAsync(Completed(), CancellationToken.None);

        airflow.Requests.TryDequeue(out var request).Should().BeTrue();
        request.Authorization.Should().Be("Bearer airflow-token");
    }

    [Fact]
    public void An_enabled_trigger_needs_a_base_url_at_startup()
    {
        using var provider = Build(baseUrl: null);

        var read = () => provider.GetRequiredService<IOptions<AirflowOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*BaseUrl*");
    }

    [Theory]
    [InlineData("bad/dag")]
    [InlineData("")]
    public void A_dag_id_must_be_a_plain_airflow_identifier(string dagId)
    {
        using var provider = Build(new Uri("http://airflow:8080"), dagId: dagId);

        var read = () => provider.GetRequiredService<IOptions<AirflowOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*DagId*");
    }
}
