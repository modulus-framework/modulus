namespace Modulus.Analytics.OpenLineage;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Analytics;

/// <summary>
/// Posts backfill run events to the OpenLineage endpoint. A post that fails is logged and dropped, never thrown: a lineage backend that is down must not
/// fail or stall a backfill. Cancellation is still honoured.
/// </summary>
internal sealed class OpenLineageEmitter(
    IHttpClientFactory httpClients,
    IEnumerable<IAnalyticsSink> sinks,
    IOptions<OpenLineageOptions> options,
    ILogger<OpenLineageEmitter> logger) : IAnalyticsLineageEmitter
{
    /// <summary>The name of the HTTP client that posts the events.</summary>
    public const string HttpClientName = "Modulus.Analytics.OpenLineage";

    public async Task EmitAsync(AnalyticsRunLineage lineage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        var settings = options.Value;
        if (!settings.Enabled || settings.Url is null)
        {
            return;
        }

        var sinkNames = sinks.Select(s => s.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var body = OpenLineageEvents.Build(settings, lineage, sinkNames);
        try
        {
            using var client = httpClients.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(settings.Url, body, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenLineage {State} event of run {Run} ({Dataset}.v{Version}) was not delivered.",
                lineage.State, lineage.RunId, lineage.Dataset, lineage.Version);
        }
    }
}
