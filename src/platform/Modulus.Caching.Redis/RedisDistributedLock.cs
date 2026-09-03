namespace Modulus.Caching.Redis;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using StackExchange.Redis;

/// <summary>
/// Redis-backed distributed lock using SET with NX (only if not exists) and EX (auto-expiry).
/// The lock value is a unique token per acquisition attempt, allowing the holder to verify
/// they still own the lock before releasing it (preventing accidental release if the lock
/// was auto-expired and reacquired by another replica).
/// </summary>
internal sealed class RedisDistributedLock(
    IConnectionMultiplexer redis,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string key,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive");

        try
        {
            var db = redis.GetDatabase();
            var lockValue = Guid.NewGuid().ToString("N");
            var seconds = (int)Math.Ceiling(duration.TotalSeconds);

            // SET key value NX EX <seconds>
            // Returns true only if the key didn't exist (lock acquired by this replica)
            var acquired = await db.StringSetAsync(
                key,
                lockValue,
                TimeSpan.FromSeconds(seconds),
                When.NotExists);

            if (!acquired)
                return null;

            return new RedisDistributedLease(db, key, lockValue, logger);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to acquire distributed lock '{key}' from Redis", ex);
        }
    }

    private sealed class RedisDistributedLease(
        IDatabase db,
        string key,
        string lockValue,
        ILogger logger)
        : IDistributedLease
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // Release only if we still own the lock (value matches).
                // If the lock auto-expired and another replica acquired it, we don't
                // accidentally release their lock.
                await db.LockReleaseAsync(key, lockValue);
            }
            catch (Exception ex)
            {
                // Log but don't throw — the lease is already expired server-side anyway.
                // Debug.WriteLine here previously — [Conditional("DEBUG")] means it was
                // compiled OUT of Release builds, so a failed release was silently
                // invisible in production with zero telemetry.
                logger.LogWarning(ex, "Failed to release distributed lock '{Key}'", key);
            }
        }
    }
}
