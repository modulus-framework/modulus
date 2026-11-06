namespace Modulus.Analytics.EntityFrameworkCore;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;

/// <summary>Recognises the failures concurrent writers cause each other, which a retry resolves: a unique-index loss and a deadlock.</summary>
internal static class AnalyticsDbRaces
{
    /// <summary>
    /// Whether <paramref name="exception"/>, or something it wraps, is a lost race rather than a real fault. Providers wrap these differently (a retrying
    /// execution strategy turns a deadlock into an <see cref="InvalidOperationException"/>), so the whole chain is searched.
    /// </summary>
    internal static bool IsRace(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            if (e is DbUpdateException)
            {
                return true;
            }

            if (e is DbException db && (db.SqlState is "40P01" or "40001" || db.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (e.InnerException is null)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>A short, jittered wait before trying again, so the writers that collided do not collide again in step.</summary>
    internal static Task BackoffAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds((15 * attempt) + Random.Shared.Next(0, 30)), cancellationToken);
}
