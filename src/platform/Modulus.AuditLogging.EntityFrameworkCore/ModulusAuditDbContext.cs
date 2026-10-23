namespace Modulus.AuditLogging.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// The audit store: the business audit log (<see cref="AuditLogs"/>) and the security audit chains
/// (<see cref="SecurityAuditEntries"/>). Derive the app's context from it (so its migrations live in the app) and
/// register it with <c>AddModulusAuditStore&lt;TContext&gt;()</c>. It has no tenant query filter on purpose: the
/// writer appends to every tenant's chain, and readers filter by tenant themselves.
/// </summary>
/// <remarks>
/// Give it its own connection string with a role that can only <c>SELECT</c> and <c>INSERT</c> the security table
/// (<see cref="SecurityAuditDatabaseScripts"/>), so the application cannot rewrite its own audit trail.
/// </remarks>
public class ModulusAuditDbContext : DbContext
{
    /// <summary>The business audit log table.</summary>
    public const string AuditLogTable = "modulus_audit_logs";

    /// <summary>The security audit chain table.</summary>
    public const string SecurityAuditTable = "modulus_security_audit";

    /// <summary>The head of the last entry retention purged from each chain (A11-1).</summary>
    public const string SecurityAuditCheckpointTable = "modulus_security_audit_checkpoints";

    /// <summary>The chains under a legal hold (A11-1): a held chain is never purged.</summary>
    public const string SecurityAuditHoldTable = "modulus_security_audit_holds";

    /// <summary>The purged entries of each chain, kept as their own verifiable record (A11-2).</summary>
    public const string SecurityAuditArchiveTable = "modulus_security_audit_archive";

    /// <summary>Creates the context.</summary>
    protected ModulusAuditDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Business audit rows of every tenant.</summary>
    public DbSet<AuditLogRow> AuditLogs => Set<AuditLogRow>();

    /// <summary>Security audit chain entries of every tenant.</summary>
    public DbSet<SecurityAuditRow> SecurityAuditEntries => Set<SecurityAuditRow>();

    /// <summary>The purge checkpoint of each chain, one row per chain.</summary>
    public DbSet<SecurityAuditCheckpointRow> SecurityAuditCheckpoints => Set<SecurityAuditCheckpointRow>();

    /// <summary>The legal holds, one row per held chain.</summary>
    public DbSet<SecurityAuditHoldRow> SecurityAuditHolds => Set<SecurityAuditHoldRow>();

    /// <summary>The archived entries, one row per entry.</summary>
    public DbSet<SecurityAuditArchiveRow> SecurityAuditArchives => Set<SecurityAuditArchiveRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        ConfigureAudit(modelBuilder, Database.ProviderName);
    }

    /// <summary>Maps the audit entities; call it from another context's <c>OnModelCreating</c> to host them there.</summary>
    public static void ConfigureAudit(ModelBuilder modelBuilder, string? providerName = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // SQLite cannot order or compare DateTimeOffset values; store them as sortable binary there.
        var sqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        var mysql = providerName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

        modelBuilder.Entity<AuditLogRow>(b =>
        {
            b.ToTable(AuditLogTable);
            b.HasKey(r => r.Id);
            b.Property(r => r.Action).HasMaxLength(200).IsRequired();
            b.Property(r => r.Resource).HasMaxLength(200);
            b.Property(r => r.ResourceId).HasMaxLength(200);
            b.Property(r => r.UserName).HasMaxLength(256);
            b.Property(r => r.Detail).HasMaxLength(4000);
            if (sqlite)
                b.Property(r => r.OccurredAt).HasConversion(new DateTimeOffsetToBinaryConverter());
            b.HasIndex(r => new { r.TenantId, r.OccurredAt });
        });

        modelBuilder.Entity<SecurityAuditRow>(b =>
        {
            b.ToTable(SecurityAuditTable);

            // The key is the uniqueness rule that keeps each chain contiguous: two writers that read the same head
            // both try to insert the next sequence, and one of them fails and retries.
            b.HasKey(r => new { r.ChainId, r.Sequence });
            b.Property(r => r.Category).HasMaxLength(100).IsRequired();
            b.Property(r => r.Action).HasMaxLength(200).IsRequired();
            b.Property(r => r.Outcome).HasMaxLength(50).IsRequired();
            b.Property(r => r.Actor).HasMaxLength(200);
            b.Property(r => r.Target).HasMaxLength(1000);
            b.Property(r => r.CorrelationId).HasMaxLength(128);
            b.Property(r => r.Details).IsRequired();
            b.Property(r => r.PreviousHash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.Property(r => r.Hash).HasMaxLength(64).IsFixedLength().IsRequired();
            // The chain hashes OccurredAt to whole milliseconds, and a read-back must return exactly the stored instant or verification fails.
            // SQLite stores binary; MySQL reads a datetime(3) back without its fraction, so it stores Unix milliseconds in a bigint; the other
            // databases keep the milliseconds in a datetime column.
            if (sqlite)
                b.Property(r => r.OccurredAt).HasConversion(new DateTimeOffsetToBinaryConverter());
            else if (mysql)
                b.Property(r => r.OccurredAt)
                    .HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v))
                    .HasColumnType("bigint");
            else
                b.Property(r => r.OccurredAt).HasPrecision(3);
        });

        modelBuilder.Entity<SecurityAuditCheckpointRow>(b =>
        {
            b.ToTable(SecurityAuditCheckpointTable);
            b.HasKey(r => r.ChainId);
            b.Property(r => r.Hash).HasMaxLength(64).IsFixedLength().IsRequired();
            if (sqlite)
                b.Property(r => r.PurgedAtUtc).HasConversion(new DateTimeOffsetToBinaryConverter());
        });

        modelBuilder.Entity<SecurityAuditArchiveRow>(b =>
        {
            b.ToTable(SecurityAuditArchiveTable);
            b.HasKey(r => new { r.ChainId, r.Sequence });
            b.Property(r => r.PreviousHash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.Property(r => r.Hash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.Property(r => r.Payload).IsRequired();
            if (sqlite)
                b.Property(r => r.ArchivedAtUtc).HasConversion(new DateTimeOffsetToBinaryConverter());
        });

        modelBuilder.Entity<SecurityAuditHoldRow>(b =>
        {
            b.ToTable(SecurityAuditHoldTable);
            b.HasKey(r => r.ChainId);
            b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
            if (sqlite)
                b.Property(r => r.PlacedAtUtc).HasConversion(new DateTimeOffsetToBinaryConverter());
        });
    }
}

/// <summary>
/// One purged security audit entry, kept whole: <see cref="Payload"/> is the entry as JSON, and the hashes let the segment be verified again
/// on its own (A11-2).
/// </summary>
public sealed class SecurityAuditArchiveRow
{
    public Guid ChainId { get; set; }

    public long Sequence { get; set; }

    public string PreviousHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ArchivedAtUtc { get; set; }
}

/// <summary>A legal hold on one chain: retention and purges leave it alone until someone releases the hold (A11-1).</summary>
public sealed class SecurityAuditHoldRow
{
    public Guid ChainId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset PlacedAtUtc { get; set; }
}

/// <summary>The purge checkpoint of one chain: the head of the last entry retention removed (A11-1).</summary>
public sealed class SecurityAuditCheckpointRow
{
    public Guid ChainId { get; set; }

    public long Sequence { get; set; }

    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset PurgedAtUtc { get; set; }
}

/// <summary>A stored <see cref="AuditLogEntry"/>.</summary>
public sealed class AuditLogRow
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Resource { get; set; }

    public string? ResourceId { get; set; }

    public string? Detail { get; set; }
}

/// <summary>A stored security audit chain entry (<see cref="Security.SecurityAuditRecord"/>).</summary>
public sealed class SecurityAuditRow
{
    public Guid ChainId { get; set; }

    public long Sequence { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? Actor { get; set; }

    public string? Target { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>The details as a JSON object.</summary>
    public string Details { get; set; } = "{}";

    public string PreviousHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}
