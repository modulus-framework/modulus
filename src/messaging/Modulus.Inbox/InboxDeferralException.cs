namespace Modulus.Inbox.Abstractions;

/// <summary>
/// Thrown by <see cref="IInboxStore.TryClaimAsync"/> when an event is already
/// being processed (or was concurrently claimed) and should be redelivered
/// later. Consumers/brokers should treat this as a transient NACK, not a
/// permanent failure.
/// </summary>
public sealed class InboxDeferralException(string message, Exception? inner = null)
    : Exception(message, inner);
