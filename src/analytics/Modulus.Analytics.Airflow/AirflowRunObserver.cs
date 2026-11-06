namespace Modulus.Analytics.Airflow;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// Starts the transform DAG for a completed backfill run through the Airflow stable REST API
/// (<c>POST /api/v1/dags/{dag_id}/dagRuns</c>). The run id is the Airflow <c>dag_run_id</c>, so a retried request for the same run is refused by Airflow
/// with 409 and counts as started.
/// </summary>
internal sealed class AirflowRunObserver(IHttpClientFactory httpClients, IOptions<AirflowOptions> options) : IAnalyticsRunObserver
{
    /// <summary>The name of the HTTP client that calls Airflow.</summary>
    public const string HttpClientName = "Modulus.Analytics.Airflow";

    public async Task OnBackfillCompletedAsync(AnalyticsRunCompleted completed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);
        var settings = options.Value;
        if (!settings.Enabled || settings.BaseUrl is null)
        {
            return;
        }

        var body = new AirflowDagRunRequest(
            DagRunId: $"modulus-{completed.RunId:N}",
            Conf: new AirflowDagRunConf(completed.Dataset, completed.Version, completed.TenantId.ToString("D"), completed.RunId.ToString("D"),
                completed.RowsQueued, settings.DbtSelector));
        var path = $"api/v1/dags/{Uri.EscapeDataString(settings.DagId)}/dagRuns";

        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(settings.BaseUrl, path), body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return; // already started for this run
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record AirflowDagRunRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("dag_run_id")] string DagRunId,
        [property: System.Text.Json.Serialization.JsonPropertyName("conf")] AirflowDagRunConf Conf);

    private sealed record AirflowDagRunConf(
        [property: System.Text.Json.Serialization.JsonPropertyName("dataset")] string Dataset,
        [property: System.Text.Json.Serialization.JsonPropertyName("version")] int Version,
        [property: System.Text.Json.Serialization.JsonPropertyName("tenant_id")] string TenantId,
        [property: System.Text.Json.Serialization.JsonPropertyName("run_id")] string RunId,
        [property: System.Text.Json.Serialization.JsonPropertyName("rows_queued")] long RowsQueued,
        [property: System.Text.Json.Serialization.JsonPropertyName("dbt_select")] string DbtSelector);
}
