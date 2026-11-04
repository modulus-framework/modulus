namespace Modulus.Analytics.Debezium;

/// <summary>Reads a batch of change events and queues the records for delivery, through the analytics queue (durable where the store is wired).</summary>
public sealed class DebeziumIngestor(DebeziumChangeReader reader, IAnalyticsQueue queue)
{
    /// <summary>Reads the events and queues their records. Events that cannot be read are returned as problems, and the rest still go through.</summary>
    /// <param name="messages">The change events.</param>
    /// <param name="cancellationToken">Cancels the queueing.</param>
    public async Task<DebeziumReadResult> IngestAsync(IEnumerable<string> messages, CancellationToken cancellationToken)
    {
        var result = reader.Read(messages);
        if (result.Records.Count > 0)
        {
            await queue.EnqueueAsync(result.Records, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
