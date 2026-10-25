namespace Modulus.EntityFrameworkCore.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;

/// <summary>
/// The cross-replica lock around the database migration run. It uses the app's <see cref="IDistributedLock"/> (Redis when
/// configured). Without one, a single node runs unlocked and a warning says so.
/// </summary>
internal static class MigrationLock
{
    /// <summary>The lock key, shared by every replica of the app.</summary>
    internal const string Key = "modulus:database-migrations";

    /// <summary>How long a holder keeps the lock if it crashes before releasing it.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>How long a replica waits for another replica's migration run before it gives up.</summary>
    internal static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns the lease once the lock is held, or null when no <see cref="IDistributedLock"/> is registered. Waits for another replica
    /// to finish; throws <see cref="TimeoutException"/> when the wait exceeds <paramref name="waitTimeout"/>.
    /// </summary>
    internal static async Task<IAsyncDisposable?> AcquireAsync(
        IServiceProvider services, TimeSpan waitTimeout, ILogger? logger, CancellationToken ct)
    {
        var locks = services.GetService<IDistributedLock>();
        if (locks is null)
        {
            logger?.LogWarning("No IDistributedLock is registered: replicas starting together migrate without a lock.");
            return null;
        }

        var deadline = DateTimeOffset.UtcNow + waitTimeout;
        while (true)
        {
            var lease = await locks.TryAcquireAsync(Key, LeaseDuration, ct);
            if (lease is not null)
                return lease;

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Another replica still holds the database migration lock; the migration was not run.");

            logger?.LogInformation("Another replica is migrating the databases; waiting for it.");
            await Task.Delay(RetryDelay, ct);
        }
    }
}
