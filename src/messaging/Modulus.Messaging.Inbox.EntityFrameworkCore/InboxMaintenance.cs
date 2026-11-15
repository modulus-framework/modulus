using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

public static class InboxMaintenance
{
    /// <summary>
    /// Deletes inbox records older than the detection window (BRS default: 7 days). After that a redelivery of the
    /// same message is applied again, so the window must be longer than the broker's longest redelivery period.
    /// </summary>
    public static Task<int> PurgeExpiredAsync<TContext>(TContext db, TimeSpan detectionWindow, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(db);
        if (detectionWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(detectionWindow), "Must be positive.");
        }

        var cutoff = DateTime.UtcNow - detectionWindow;
        return db.Set<InboxRecord>()
            .Where(r => r.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
