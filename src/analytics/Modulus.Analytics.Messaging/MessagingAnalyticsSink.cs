namespace Modulus.Analytics.Messaging;

using System.Text.Json;
using Modulus.Events.Abstractions;

/// <summary>
/// Publishes each analytics record as an <see cref="AnalyticsRecordPublished"/> integration event on <see cref="IModuleBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// The sink is append-only: it cannot erase a tenant. Registering it therefore blocks tenant erasure (<c>AnalyticsTenantDataHandler</c> refuses
/// a sink that cannot erase), because the consumer's copy would survive. Enable it only where the consumer also handles erasure.
/// </para>
/// <para>
/// A batch is published record by record. If publishing fails part way, the batch is retried in full, so a consumer sees some records twice.
/// Consumers must deduplicate, as the event documentation says.
/// </para>
/// </remarks>
public sealed class MessagingAnalyticsSink(IModuleBus bus) : IAnalyticsSink
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string Name => "messaging";

    /// <inheritdoc />
    public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        // The bus carries any dataset, so there is no table to prepare.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var values = JsonSerializer.Serialize(record.Values, s_json);
                var message = new AnalyticsRecordPublished(record.Dataset, record.SchemaVersion, record.TenantId, record.Revision, values)
                {
                    EventId = record.EventId,
                    OccurredAt = record.OccurredAt.UtcDateTime,
                };
                await bus.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AnalyticsSinkException(
                    $"Could not publish record {record.EventId} of '{dataset.Key}' to the message bus.", isTransient: true, ex);
            }
        }
    }
}
