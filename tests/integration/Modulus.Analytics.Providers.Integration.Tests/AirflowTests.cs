namespace Modulus.Analytics.Providers.Integration.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Airflow;
using Xunit;

/// <summary>The Airflow run observer against a real Airflow: it starts the DAG with the run's facts, and a repeat for the same run counts as started.</summary>
[Trait("Category", "Integration")]
public sealed class AirflowTests : IAsyncLifetime
{
    private const int Port = 8080;

    private readonly IContainer _airflow = new ContainerBuilder("docker.io/apache/airflow:2.10.4")
        .WithEnvironment("AIRFLOW__API__AUTH_BACKENDS", "airflow.api.auth.backend.basic_auth")
        .WithEnvironment("AIRFLOW__CORE__LOAD_EXAMPLES", "False")
        .WithEnvironment("AIRFLOW__CORE__EXECUTOR", "SequentialExecutor")
        .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "Assets", "modulus_analytics_transform.py"), "/opt/airflow/dags/")
        .WithPortBinding(Port, true)
        .WithEntrypoint("bash", "-c")
        .WithCommand("airflow db migrate && airflow users create --username admin --password admin --firstname a --lastname b --role Admin --email a@b.c && (airflow scheduler & exec airflow webserver)")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/health")))
        .Build();

    public Task InitializeAsync() => _airflow.StartAsync();

    public Task DisposeAsync() => _airflow.DisposeAsync().AsTask();

    [Fact]
    public async Task A_completed_run_starts_the_dag_and_a_repeat_is_counted_as_started()
    {
        var baseUrl = new Uri($"http://{_airflow.Hostname}:{_airflow.GetMappedPublicPort(Port)}/");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAirflow(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Airflow:Enabled"] = "true",
            ["Analytics:Airflow:BaseUrl"] = baseUrl.ToString(),
            ["Analytics:Airflow:Username"] = "admin",
            ["Analytics:Airflow:Password"] = "admin",
        }).Build());
        await using var provider = services.BuildServiceProvider();
        var observer = provider.GetRequiredService<IAnalyticsRunObserver>();
        var run = new AnalyticsRunCompleted(Guid.NewGuid(), "orders", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, 42);

        await observer.OnBackfillCompletedAsync(run, CancellationToken.None);
        await observer.OnBackfillCompletedAsync(run, CancellationToken.None); // Airflow answers 409; that counts as started

        using var http = new HttpClient { BaseAddress = baseUrl };
        http.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String("admin:admin"u8.ToArray()));
        var dagRun = await http.GetFromJsonAsync<JsonElement>($"api/v1/dags/modulus_analytics_transform/dagRuns/modulus-{run.RunId:N}");
        dagRun.GetProperty("conf").GetProperty("tenant_id").GetString().Should().Be(run.TenantId.ToString("D"));
        dagRun.GetProperty("conf").GetProperty("rows_queued").GetInt64().Should().Be(42);
    }
}
