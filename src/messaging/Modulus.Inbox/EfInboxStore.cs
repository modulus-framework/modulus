namespace Modulus.Inbox;

using Microsoft.EntityFrameworkCore;
using Modulus.Inbox.Abstractions;

/// <summary>
/// EF Core implementation of <see cref="IInboxStore"/>. Uses the
/// <see cref="InboxMessage"/> PK (EventId) for atomic claim semantics:
/// concurrent inserts race on a single INSERT, the loser gets a
/// <see cref="DbUpdateException"/> and defers.
/// </summary>
internal sealed class EfInboxStore(DbContext db) : IInboxStore
{
    public async Task<InboxMessage?> TryClaimAsync(
        Guid eventId,
        string messageType,
        string payload,
        int maxRetries,
        CancellationToken ct)
    {
        var existing = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId, ct);

        if (existing is { Status: InboxStatus.Processed })
            return null;

        if (existing is { } prev && prev.RetryCount >= maxRetries)
            return null;

        if (existing is null)
        {
            // New record — insert atomically. A concurrent insert on the same
            // PK (EventId) races; the loser gets a DbUpdateException → defer.
            var inbox = new InboxMessage
            {
                Id = eventId,
                MessageType = messageType,
                Payload = payload,
                ModuleName = db.GetType().Name.Replace("DbContext", string.Empty),
                Status = InboxStatus.Processing,
            };
            db.Set<InboxMessage>().Add(inbox);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is being processed by another consumer.");
            }

            return inbox;
        }

        if (existing.Status == InboxStatus.Processing)
            throw new InboxDeferralException(
                $"Inbox message {eventId} is already being processed.");

        // Existing Pending/Failed — atomically claim via a server-side UPDATE
        // whose WHERE re-checks the status. Two consumers that both read the
        // same Failed row cannot both win: only the Pending/Failed status
        // matches, and the second UPDATE affects zero rows → defer.
        var rowsAffected = await db.Set<InboxMessage>()
            .Where(m => m.Id == eventId
                     && (m.Status == InboxStatus.Pending
                         || m.Status == InboxStatus.Failed))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, InboxStatus.Processing)
                      .SetProperty(m => m.Error, (string?)null),
                ct);

        if (rowsAffected == 0)
            throw new InboxDeferralException(
                $"Inbox message {eventId} is being processed by another consumer.");

        return new InboxMessage
        {
            Id = eventId,
            MessageType = existing.MessageType,
            Payload = existing.Payload,
            Status = InboxStatus.Processing,
            ReceivedAt = existing.ReceivedAt,
            RetryCount = existing.RetryCount,
        };
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        var msg = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg is null) return;
        msg.Status = InboxStatus.Processed;
        msg.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct)
    {
        var msg = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg is null) return;
        msg.Status = InboxStatus.Failed;
        msg.Error = error;
        msg.RetryCount += 1;
        await db.SaveChangesAsync(ct);
    }
}
