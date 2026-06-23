using Microsoft.Extensions.Logging;

namespace Modulus.Inbox;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Events.Abstractions;
using Modulus.Inbox.Abstractions;

/// <summary>
/// Decorator that wraps IIntegrationEventHandler{T} with inbox deduplication.
/// Registered automatically by AddInbox{TContext}() for all handlers.
/// </summary>
public sealed class IdempotentIntegrationEventHandler<TEvent>(
    IIntegrationEventHandler<TEvent>                      inner,
    DbContext                                              db,
    ILogger<IdempotentIntegrationEventHandler<TEvent>>     logger)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        // 1. Check for existing inbox record
        var existing = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == @event.EventId, ct);

        if (existing?.Status == InboxStatus.Processed)
        {
            logger.LogDebug(
                "Inbox: duplicate {Type} {Id} skipped.",
                typeof(TEvent).Name, @event.EventId);
            return;
        }

        // 2. Upsert inbox record — mark Processing
        InboxMessage inbox;
        if (existing is null)
        {
            inbox = new InboxMessage
            {
                Id          = @event.EventId,
                MessageType = typeof(TEvent).AssemblyQualifiedName!,
                Payload     = JsonSerializer.Serialize(@event),
                ModuleName  = db.GetType().Name.Replace("DbContext", ""),
            };
            db.Set<InboxMessage>().Add(inbox);
        }
        else
        {
            inbox = existing;
        }

        inbox.Status = InboxStatus.Processing;
        await db.SaveChangesAsync(ct);

        // 3. Execute the real handler
        try
        {
            await inner.HandleAsync(@event, ct);
            inbox.Status      = InboxStatus.Processed;
            inbox.ProcessedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            inbox.Status     = InboxStatus.Failed;
            inbox.Error      = ex.Message;
            inbox.RetryCount++;
            logger.LogError(ex,
                "Inbox: handler failed for {Type} {Id}",
                typeof(TEvent).Name, @event.EventId);
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }
}