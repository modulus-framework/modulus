using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modulus.Inbox;

using System.Text.Json;
using Modulus.Events.Abstractions;
using Modulus.Inbox.Abstractions;
using Modulus.Observability;

/// <summary>
/// Decorator that wraps <see cref="IIntegrationEventHandler{TEvent}"/> with
/// inbox deduplication. Registered automatically by <c>AddInbox</c> /
/// <c>AddMongoInbox</c> for all handlers.
/// <para>
/// Dedup is anchored on <see cref="IIntegrationEvent.EventId"/>, which becomes
/// the <see cref="InboxMessage"/> primary key. This gives a database-level
/// uniqueness guarantee: two concurrent deliveries of the same event race on
/// the claim, so only one wins and executes the inner handler.
/// </para>
/// <para>
/// Behaviour by state of the existing inbox row for an EventId:
/// <list type="bullet">
///   <item><c>Processed</c> — duplicate; skipped (inner NOT executed).</item>
///   <item><c>Processing</c> — in-flight elsewhere; deferred (throws
///       <see cref="InboxDeferralException"/>) so the broker redelivers later,
///       rather than executing the side effect twice.</item>
///   <item><c>RetryCount &gt;= MaxRetries</c> — poison message; dead-lettered
///       (inner NOT executed) to stop a perpetually-failing event hot-looping.</item>
///   <item>otherwise — the row is claimed (inserted or transitioned to
///       <c>Processing</c>), the inner handler runs, and the final state is
///       persisted.</item>
/// </list>
/// </para>
/// </summary>
public sealed class IdempotentIntegrationEventHandler<TEvent>(
    IIntegrationEventHandler<TEvent> inner,
    IInboxStore store,
    IOptions<InboxOptions> opts,
    ILogger<IdempotentIntegrationEventHandler<TEvent>> logger)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        var id = @event.EventId;

        // 1. Atomically claim (or skip/defer).  The store handles all
        //    storage-specific logic (PK race, status check, retry budget).
        InboxMessage? claimed;
        try
        {
            claimed = await store.TryClaimAsync(
                id,
                IntegrationEventNaming.GetName(typeof(TEvent)),
                JsonSerializer.Serialize(@event),
                opts.Value.MaxRetries,
                ct);
        }
        catch (InboxDeferralException)
        {
            logger.LogDebug("Inbox: {Type} {Id} in-flight elsewhere; deferring.",
                typeof(TEvent).Name, id);
            throw;
        }

        if (claimed is null)
        {
            ModulusMeters.InboxDedupHits.Add(1);
            logger.LogDebug("Inbox: {Type} {Id} skipped (duplicate or dead-lettered).",
                typeof(TEvent).Name, id);
            return;
        }

        // 2. We own the record — execute the real handler. Capture any error
        //    so we can persist final state AND re-throw the ORIGINAL exception
        //    (a failure in the persistence step below must not mask it).
        Exception? handlerError = null;
        try
        {
            await inner.HandleAsync(@event, ct);
        }
        catch (Exception ex)
        {
            handlerError = ex;
            logger.LogError(ex,
                "Inbox: handler failed for {Type} {Id}",
                typeof(TEvent).Name, id);
        }

        // 3. Persist final state in its own try/catch. A persistence failure
        //    here is logged but does NOT replace the handler's exception.
        try
        {
            if (handlerError is null)
                await store.MarkProcessedAsync(id, ct);
            else
                await store.MarkFailedAsync(id, handlerError.Message, ct);
        }
        catch (Exception persistEx)
        {
            logger.LogError(persistEx,
                "Inbox: failed to persist final state for {Type} {Id}",
                typeof(TEvent).Name, id);
        }

        if (handlerError is not null)
            throw handlerError;
    }
}
