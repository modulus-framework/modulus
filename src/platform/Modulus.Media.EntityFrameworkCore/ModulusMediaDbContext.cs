namespace Modulus.Media.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The media metadata table. Derive the application's context from it, so its migrations live in the application, and register it with
/// <c>AddModulusMediaStore&lt;TContext&gt;()</c>. The bytes stay in <see cref="Modulus.Storage.IFileStorage"/>; this table only holds metadata.
/// </summary>
public class ModulusMediaDbContext : DbContext
{
    /// <summary>The metadata table.</summary>
    public const string MediaTable = "modulus_media";

    /// <summary>Creates the context.</summary>
    protected ModulusMediaDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The metadata rows of every company. Queries filter by company in the store.</summary>
    public DbSet<MediaRow> MediaItems => Set<MediaRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaRow>(b =>
        {
            b.ToTable(MediaTable);
            b.HasKey(m => m.Id);
            b.Property(m => m.FileName).HasMaxLength(255).IsRequired();
            b.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
            b.Property(m => m.Sha256).HasMaxLength(64).IsRequired();
            b.Property(m => m.StorageKey).HasMaxLength(512).IsRequired();

            // Stored as UTC ticks: every provider sorts a bigint, but not every provider sorts a DateTimeOffset (SQLite refuses it in ORDER BY).
            b.Property(m => m.UploadedAt).HasConversion(v => v.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

            // The company is the first column of the lookups, so one company's list is an index range.
            b.HasIndex(m => new { m.TenantId, m.UploadedAt });
        });
    }
}

/// <summary>A metadata row, mapped to and from <see cref="MediaItem"/>.</summary>
public sealed class MediaRow
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public MediaKind Kind { get; set; }

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";

    public string StorageKey { get; set; } = "";

    public Guid? UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public static MediaRow From(MediaItem item) => new()
    {
        Id = item.Id,
        TenantId = item.TenantId,
        FileName = item.FileName,
        ContentType = item.ContentType,
        Kind = item.Kind,
        Size = item.Size,
        Sha256 = item.Sha256,
        StorageKey = item.StorageKey,
        UploadedBy = item.UploadedBy,
        UploadedAt = item.UploadedAt,
    };

    public MediaItem ToItem() => new(Id, TenantId, FileName, ContentType, Kind, Size, Sha256, StorageKey, UploadedBy, UploadedAt);
}
