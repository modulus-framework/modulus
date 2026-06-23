namespace Modulus.Outbox.Abstractions;

using Modulus.Events.Abstractions;

public interface IOutboxWriter
{
    /// <summary>
    /// Appends an outbox record to the current DB context.
    /// Must be called BEFORE UoW.CommitAsync() so both are in the same transaction.
    /// </summary>
    Task WriteAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}

public interface IOutboxDispatcher
{
    Task DispatchAsync(
        OutboxMessage message,
        CancellationToken ct);
}