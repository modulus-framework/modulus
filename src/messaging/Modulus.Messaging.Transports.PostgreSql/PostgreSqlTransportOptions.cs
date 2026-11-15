using System.Text.RegularExpressions;

namespace Modulus.Messaging.Transports.PostgreSql;

/// <summary>Connection and queue settings. The connection string carries credentials: keep it in configuration or a secret store (NFR-09).</summary>
public sealed partial class PostgreSqlTransportOptions
{
    /// <summary>Connection string of the database that holds the queue table. The role must not own the table (see the tenant docs).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Schema of the queue table.</summary>
    public string Schema { get; set; } = "public";

    /// <summary>Name of the queue table. All endpoints share it, told apart by the <c>queue</c> column.</summary>
    public string Table { get; set; } = "messaging_queue";

    /// <summary>Creates the table and index on start when missing. Turn off when a migration owns the schema.</summary>
    public bool CreateSchema { get; set; } = true;

    /// <summary>Messages claimed per poll, per endpoint.</summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>How long an endpoint waits before polling again when its queue is empty or the database is unreachable.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a claimed message stays with its host. A host that dies mid-message loses its claim after this time, and
    /// another host receives the message again (at-least-once). Set it above the longest consume time.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a stop waits for in-flight messages before they are released for another host (NFR-18).</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("ConnectionString is required.", nameof(ConnectionString));
        }

        if (!Identifier().IsMatch(Schema))
        {
            throw new ArgumentException("Schema must be a plain SQL identifier (letters, digits, underscores).", nameof(Schema));
        }

        if (!Identifier().IsMatch(Table))
        {
            throw new ArgumentException("Table must be a plain SQL identifier (letters, digits, underscores).", nameof(Table));
        }

        if (BatchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "Must be between 1 and 1000.");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), "Must be positive.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), "Must be positive.");
        }

        if (StopTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StopTimeout), "Must not be negative.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
