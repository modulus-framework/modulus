namespace Modulus.EventBus.Kafka;

using Modulus.Events.Abstractions;

/// <summary>
/// Determines which Kafka partition a message goes to. Overrides the default
/// one-partition-per-event-type behavior so events from the same aggregate
/// land on the same partition (total ordering) and hot-partition risk is spread.
/// </summary>
public interface IPartitionKeyProvider
{
    /// <summary>
    /// Returns the partition key for <paramref name="event"/>.
    /// Messages with the same key go to the same partition.
    /// </summary>
    string GetPartitionKey(IIntegrationEvent @event);
}
