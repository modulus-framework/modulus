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

    /// <summary>
    /// Turns on row-level isolation of the queue table by company (FR-21). Off by default, so existing deployments are
    /// unchanged. The owner creates the policy: the transport creates it on start only when it also creates the schema. The transport connects as a member of <see cref="TransportRole"/>, which sees every row; any other
    /// session sees only its company's rows (<c>set_config('modulus.tenant', ..., false)</c>). Read
    /// docs/messaging/postgresql-tenant-rls.sql for the policy.
    /// </summary>
    public bool TenantRowLevelSecurity { get; set; }

    /// <summary>The role whose members see every queue row (the transport). Created on start when missing.</summary>
    public string TransportRole { get; set; } = "modulus_transport";

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

    /// <summary>
    /// How long parked messages (<c>_error</c>, <c>_skipped</c>) are kept. When set, the transport purges older ones while
    /// it runs, every <see cref="PurgeInterval"/>. Null (the default) keeps them until a job calls <c>PurgeParkedAsync</c>.
    /// </summary>
    public TimeSpan? ParkedRetention { get; set; }

    /// <summary>How often the automatic purge of parked messages runs, when <see cref="ParkedRetention"/> is set.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Called when an automatic purge fails. The purge is tried again at the next interval.</summary>
    public Action<Exception>? PurgeFailed { get; set; }

    /// <summary>
    /// How often the transport counts the rows of each queue for the <c>modulus.messaging.queue.depth</c> gauge. The
    /// gauge reports the last count, so a slow count never blocks a metrics scrape. Null turns the sampling off.
    /// </summary>
    public TimeSpan? QueueDepthInterval { get; set; } = TimeSpan.FromSeconds(15);

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

        if (!Identifier().IsMatch(TransportRole))
        {
            throw new ArgumentException("TransportRole must be a plain SQL identifier (letters, digits, underscores).", nameof(TransportRole));
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

        if (ParkedRetention is { } retention && retention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ParkedRetention), "Must not be negative.");
        }

        if (QueueDepthInterval is { } depthInterval && depthInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueDepthInterval), "Must be positive, or null to turn the sampling off.");
        }

        if (PurgeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PurgeInterval), "Must be positive.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
