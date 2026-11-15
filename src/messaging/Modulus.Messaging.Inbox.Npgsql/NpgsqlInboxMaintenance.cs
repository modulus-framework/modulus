using System.Text.RegularExpressions;
using Npgsql;

namespace Modulus.Messaging.Inbox.Npgsql;

/// <summary>Removes inbox records outside the detection window (BRS default: 7 days).</summary>
public static partial class NpgsqlInboxMaintenance
{
    /// <summary>Deletes the records received before the detection window, and returns how many were removed.</summary>
    public static async Task<int> PurgeExpiredAsync(NpgsqlDataSource dataSource, TimeSpan detectionWindow, CancellationToken cancellationToken, string table = "messaging_inbox")
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (detectionWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(detectionWindow), "Must be positive.");
        }

        if (!Identifier().IsMatch(table))
        {
            throw new ArgumentException("Table must be a plain SQL identifier.", nameof(table));
        }

        await using var command = dataSource.CreateCommand(NpgsqlInboxSql.Purge(table));
        command.Parameters.AddWithValue("window_ms", detectionWindow.TotalMilliseconds);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
