using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

/// <summary>
/// Sends committed outbox rows. Each round claims a batch under a lease, sends it to the transport and deletes
/// each row after its send. A crash between send and delete sends the message again, so consumers must be
/// idempotent (at-least-once, with the inbox for dedup).
/// </summary>
public static class OutboxDispatch
{
    /// <summary>Claims up to <paramref name="batchSize"/> rows, sends them and returns how many were sent.</summary>
    public static async Task<int> DispatchOnceAsync<TContext>(
        TContext db,
        IMessageTransport transport,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transport);
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Must be positive.");
        }

        var rows = db.Set<OutboxRecord>();
        var now = DateTime.UtcNow;
        var candidates = await rows
            .Where(r => !r.Parked && (r.LockedUntil == null || r.LockedUntil < now) && (r.DeliverAt == null || r.DeliverAt <= now))
            .OrderBy(r => r.EnqueuedAt)
            .Select(r => r.MessageId)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Claim: only rows still unclaimed get our token, so two dispatchers never send the same row twice in one lease.
        var token = Guid.NewGuid();
        await rows
            .Where(r => candidates.Contains(r.MessageId) && !r.Parked && (r.LockedUntil == null || r.LockedUntil < now) && (r.DeliverAt == null || r.DeliverAt <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LockToken, token)
                .SetProperty(r => r.LockedUntil, now + leaseDuration), cancellationToken)
            .ConfigureAwait(false);

        var claimed = await rows
            .Where(r => r.LockToken == token)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sent = 0;
        foreach (var row in claimed)
        {
            if (!TryRebuild(row, out var envelope))
            {
                await rows
                    .Where(r => r.MessageId == row.MessageId && r.LockToken == token)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Parked, true)
                        .SetProperty(r => r.LockToken, (Guid?)null)
                        .SetProperty(r => r.LockedUntil, (DateTime?)null), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            await transport.Send(envelope, cancellationToken).ConfigureAwait(false);
            await rows
                .Where(r => r.MessageId == row.MessageId && r.LockToken == token)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    private static bool TryRebuild(OutboxRecord row, out MessageEnvelope envelope)
    {
        envelope = null!;
        var type = Type.GetType(row.MessageType);
        if (type is null)
        {
            return false;
        }

        var message = JsonSerializer.Deserialize(row.Body, type);
        if (message is null)
        {
            return false;
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Headers) ?? [];
        envelope = new MessageEnvelope(row.MessageId, row.Endpoint, message, headers, row.ConsumerKey);
        return true;
    }
}
