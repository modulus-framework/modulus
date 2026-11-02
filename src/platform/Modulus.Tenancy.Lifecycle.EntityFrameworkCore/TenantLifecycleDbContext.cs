namespace Modulus.Tenancy.Lifecycle.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// The host database's lifecycle tables. The application derives its own context (so its migrations live in the application),
/// registers it, and calls <c>AddEfTenantLifecycleStore</c>. Tables are prefixed <c>modulus_lifecycle_</c>.
/// </summary>
public class TenantLifecycleDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>Lifecycle operations.</summary>
    public DbSet<OperationRow> LifecycleOperations => Set<OperationRow>();

    /// <summary>Legal holds.</summary>
    public DbSet<LegalHoldRow> LegalHolds => Set<LegalHoldRow>();

    /// <summary>Pending deletions.</summary>
    public DbSet<PendingDeletionRow> PendingDeletions => Set<PendingDeletionRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OperationRow>(e =>
        {
            e.ToTable("modulus_lifecycle_operations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.State);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
            e.Property(x => x.ExportPath).HasMaxLength(512);
            e.Property(x => x.Error).HasMaxLength(256);
            e.Property(x => x.CertificateKeyId).HasMaxLength(128);
            e.Property(x => x.StepsJson).HasColumnType("text");
            e.Property(x => x.OutcomesJson).HasColumnType("text");
            e.Property(x => x.CertificatePayload).HasColumnType("text");
            e.Property(x => x.CertificateSignature).HasMaxLength(512);
        });

        modelBuilder.Entity<LegalHoldRow>(e =>
        {
            e.ToTable("modulus_lifecycle_legal_holds");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.PlacedBy).HasMaxLength(256);
            e.Property(x => x.ReleasedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<PendingDeletionRow>(e =>
        {
            e.ToTable("modulus_lifecycle_pending_deletions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DoneAt, x.DeleteAfter });
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.Module).HasMaxLength(128);
            e.Property(x => x.Dataset).HasMaxLength(128);
        });

        // Every timestamp is stored as a UTC DateTime. Providers differ in how they order and compare DateTimeOffset (SQLite cannot
        // order it at all), and the lifecycle queries compare instants, so one representation is used everywhere.
        var toUtc = new ValueConverter<DateTimeOffset, DateTime>(
            v => v.UtcDateTime,
            v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        var toUtcOrNull = new ValueConverter<DateTimeOffset?, DateTime?>(
            v => v.HasValue ? v.Value.UtcDateTime : null,
            v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(toUtc);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(toUtcOrNull);
            }
        }
    }
}

/// <summary>A lifecycle operation row. Steps and outcomes are stored as JSON.</summary>
public sealed class OperationRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int Kind { get; set; }
    public int State { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool ExportFirst { get; set; }
    public string? ExportPath { get; set; }
    public DateTimeOffset? ExportExpiresAt { get; set; }
    public string? Error { get; set; }
    public string StepsJson { get; set; } = "[]";
    public string OutcomesJson { get; set; } = "[]";
    public string? CertificatePayload { get; set; }
    public string? CertificateSignature { get; set; }
    public string? CertificateKeyId { get; set; }
}

/// <summary>A legal hold row.</summary>
public sealed class LegalHoldRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PlacedBy { get; set; } = string.Empty;
    public DateTimeOffset PlacedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

/// <summary>A pending deletion row.</summary>
public sealed class PendingDeletionRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public DateTimeOffset DeleteAfter { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public long? Rows { get; set; }
}
