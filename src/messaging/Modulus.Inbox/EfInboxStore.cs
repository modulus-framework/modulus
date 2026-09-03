namespace Modulus.Inbox;

using Microsoft.EntityFrameworkCore;
using Modulus.Inbox.Abstractions;

/// <summary>
/// EF Core implementation of <see cref="IInboxStore"/>. Uses the
/// <see cref="InboxMessage"/> composite PK (EventId, HandlerName) for atomic
/// claim semantics: concurrent inserts race on a single INSERT, the loser gets
/// a <see cref="DbUpdateException"/> and defers.
/// </summary>
internal sealed class EfInboxStore(DbContext db) : IInboxStore
{
    public async Task<InboxMessage?> TryClaimAsync(
        Guid eventId,
        string handlerName,
        string messageType,
        string payload,
        int maxRetries,
        TimeSpan claimTimeout,
        CancellationToken ct)
    {
        var existing = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId && m.HandlerName == handlerName, ct);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            // No row for THIS handler yet. Before inserting a fresh one, check
            // for a legacy row (written before HandlerName existed, so "") for
            // the same EventId: it was claimed under the old single-key scheme
            // and must be honoured for every handler so an in-flight upgrade
            // neither reprocesses an already-handled event nor drops one still
            // mid-flight.
            var legacy = await db.Set<InboxMessage>()
                .FirstOrDefaultAsync(m => m.Id == eventId && m.HandlerName == string.Empty, ct);

            if (legacy is { Status: InboxStatus.Processed })
                return null;

            if (legacy is { } prevLegacy && prevLegacy.RetryCount >= maxRetries)
                return null;

            if (legacy is { Status: InboxStatus.Processing } processingLegacy)
            {
                var claimedAt = processingLegacy.ClaimedAt ?? processingLegacy.ReceivedAt;
                if (now - claimedAt < claimTimeout)
                    throw new InboxDeferralException(
                        $"Inbox message {eventId} is already being processed.");

                // Lease expired. Adopt the legacy row for this handler instead
                // of leaving it wedged forever. Optimistic: the WHERE re-checks
                // ClaimedAt so a concurrent adopter/renewer cannot be usurped;
                // if we lose the race, fall through to a fresh INSERT below.
                // Adoption already renamed the legacy row's key out from under
                // it, so our own (eventId, handlerName) insert cannot conflict.
                var adopted = await db.Set<InboxMessage>()
                    .Where(m => m.Id == eventId
                             && m.HandlerName == string.Empty
                             && m.Status == InboxStatus.Processing
                             && m.ClaimedAt == processingLegacy.ClaimedAt)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(m => m.HandlerName, handlerName)
                              .SetProperty(m => m.ClaimedAt, now)
                              .SetProperty(m => m.Error, (string?)null),
                        ct);

                if (adopted == 1)
                    return new InboxMessage
                    {
                        Id = eventId,
                        HandlerName = handlerName,
                        MessageType = processingLegacy.MessageType,
                        Payload = processingLegacy.Payload,
                        Status = InboxStatus.Processing,
                        ReceivedAt = processingLegacy.ReceivedAt,
                        RetryCount = processingLegacy.RetryCount,
                        ClaimedAt = now,
                    };
                // adopted == 0: lost the race, fall through to a fresh insert.
            }
            else if (legacy is { } pendingOrFailedLegacy)
            {
                // Pending/Failed legacy row, under the retry budget: adopt it
                // for this handler. Two handlers racing on the same legacy row
                // cannot both win: only the first UPDATE matches (WHERE
                // re-checks HandlerName == "" server-side); the loser falls
                // through to its own fresh INSERT below.
                var adopted = await db.Set<InboxMessage>()
                    .Where(m => m.Id == eventId
                             && m.HandlerName == string.Empty
                             && (m.Status == InboxStatus.Pending || m.Status == InboxStatus.Failed))
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(m => m.HandlerName, handlerName)
                              .SetProperty(m => m.Status, InboxStatus.Processing)
                              .SetProperty(m => m.ClaimedAt, now)
                              .SetProperty(m => m.Error, (string?)null),
                        ct);

                if (adopted == 1)
                    return new InboxMessage
                    {
                        Id = eventId,
                        HandlerName = handlerName,
                        MessageType = pendingOrFailedLegacy.MessageType,
                        Payload = pendingOrFailedLegacy.Payload,
                        Status = InboxStatus.Processing,
                        ReceivedAt = pendingOrFailedLegacy.ReceivedAt,
                        RetryCount = pendingOrFailedLegacy.RetryCount,
                        ClaimedAt = now,
                    };
                // adopted == 0: lost the race, fall through to a fresh insert.
            }

            // New record. Insert atomically: a concurrent insert on the same
            // PK (EventId, HandlerName) races, the loser gets a
            // DbUpdateException and defers.
            var inbox = new InboxMessage
            {
                Id = eventId,
                HandlerName = handlerName,
                MessageType = messageType,
                Payload = payload,
                ModuleName = db.GetType().Name.Replace("DbContext", string.Empty),
                Status = InboxStatus.Processing,
                ClaimedAt = now,
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

        if (existing is { Status: InboxStatus.Processed })
            return null;

        if (existing is { } prev && prev.RetryCount >= maxRetries)
            return null;

        if (existing.Status == InboxStatus.Processing)
        {
            // Lease check: a claim held past the timeout means the claimant
            // crashed between claiming and persisting the final state. Reclaim
            // it (optimistically, the WHERE re-checks ClaimedAt so a consumer
            // that renewed or a competing reclaimer cannot be usurped) instead
            // of deferring forever. Legacy rows (ClaimedAt null) fall back to
            // ReceivedAt as the claim time.
            var claimedAt = existing.ClaimedAt ?? existing.ReceivedAt;
            if (now - claimedAt < claimTimeout)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is already being processed.");

            var reclaimed = await db.Set<InboxMessage>()
                .Where(m => m.Id == eventId
                         && m.HandlerName == handlerName
                         && m.Status == InboxStatus.Processing
                         && m.ClaimedAt == existing.ClaimedAt)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.ClaimedAt, now)
                          .SetProperty(m => m.Error, (string?)null),
                    ct);

            if (reclaimed == 0)
                throw new InboxDeferralException(
                    $"Inbox message {eventId} is being processed by another consumer.");

            return new InboxMessage
            {
                Id = eventId,
                HandlerName = handlerName,
                MessageType = existing.MessageType,
                Payload = existing.Payload,
                Status = InboxStatus.Processing,
                ReceivedAt = existing.ReceivedAt,
                RetryCount = existing.RetryCount,
                ClaimedAt = now,
            };
        }

        // Existing Pending/Failed — atomically claim via a server-side UPDATE
        // whose WHERE re-checks the status. Two consumers that both read the
        // same Failed row cannot both win: only the Pending/Failed status
        // matches, and the second UPDATE affects zero rows so it defers.
        var rowsAffected = await db.Set<InboxMessage>()
            .Where(m => m.Id == eventId
                     && m.HandlerName == handlerName
                     && (m.Status == InboxStatus.Pending
                         || m.Status == InboxStatus.Failed))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, InboxStatus.Processing)
                      .SetProperty(m => m.ClaimedAt, now)
                      .SetProperty(m => m.Error, (string?)null),
                ct);

        if (rowsAffected == 0)
            throw new InboxDeferralException(
                $"Inbox message {eventId} is being processed by another consumer.");

        return new InboxMessage
        {
            Id = eventId,
            HandlerName = handlerName,
            MessageType = existing.MessageType,
            Payload = existing.Payload,
            Status = InboxStatus.Processing,
            ReceivedAt = existing.ReceivedAt,
            RetryCount = existing.RetryCount,
            ClaimedAt = now,
        };
    }

    public async Task MarkProcessedAsync(Guid eventId, string handlerName, CancellationToken ct)
    {
        var msg = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId && m.HandlerName == handlerName, ct);
        if (msg is null) return;
        msg.Status = InboxStatus.Processed;
        msg.ProcessedAt = DateTime.UtcNow;
        msg.ClaimedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid eventId, string handlerName, string error, CancellationToken ct)
    {
        var msg = await db.Set<InboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == eventId && m.HandlerName == handlerName, ct);
        if (msg is null) return;
        msg.Status = InboxStatus.Failed;
        msg.Error = error;
        msg.RetryCount += 1;
        msg.ClaimedAt = null;
        await db.SaveChangesAsync(ct);
    }
}
