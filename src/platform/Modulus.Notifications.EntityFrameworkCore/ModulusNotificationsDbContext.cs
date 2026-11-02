namespace Modulus.Notifications.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The notification tables: the inbox and the channel preferences. Derive the application's context from it and register it with
/// <c>AddModulusNotificationsStore&lt;TContext&gt;()</c>.
/// </summary>
public class ModulusNotificationsDbContext : DbContext
{
    /// <summary>The inbox table.</summary>
    public const string NotificationTable = "modulus_notifications";

    /// <summary>The channel preference table.</summary>
    public const string PreferenceTable = "modulus_notification_preferences";

    /// <summary>Creates the context.</summary>
    protected ModulusNotificationsDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The inbox rows of every person and company. Queries filter by person and company in the store.</summary>
    public DbSet<NotificationRow> Notifications => Set<NotificationRow>();

    /// <summary>The channel choices, one row per person, company, category and channel.</summary>
    public DbSet<NotificationPreferenceRow> Preferences => Set<NotificationPreferenceRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NotificationRow>(b =>
        {
            b.ToTable(NotificationTable);
            b.HasKey(n => n.Id);
            b.Property(n => n.Category).HasMaxLength(100);
            b.Property(n => n.Title).HasMaxLength(200).IsRequired();
            b.Property(n => n.Message).HasMaxLength(2000);
            // Timestamps are UTC ticks: plain bigint columns sort on every provider and work with ExecuteUpdate on every supported EF Core version.
            b.HasIndex(n => new { n.UserId, n.TenantId, n.CreatedAtTicks });
        });

        modelBuilder.Entity<NotificationPreferenceRow>(b =>
        {
            b.ToTable(PreferenceTable);
            b.HasKey(p => p.Id);
            b.Property(p => p.Category).HasMaxLength(100).IsRequired();
            b.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.UserId, p.Category, p.Channel }).IsUnique();
        });
    }
}

/// <summary>An inbox row, mapped to and from <see cref="UserNotification"/>.</summary>
public sealed class NotificationRow
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    public NotificationSeverity Severity { get; set; }

    public string Title { get; set; } = "";

    public string? Message { get; set; }

    public string? Category { get; set; }

    /// <summary>When it was sent, as UTC ticks.</summary>
    public long CreatedAtTicks { get; set; }

    /// <summary>When it was read, as UTC ticks, or null while unread.</summary>
    public long? ReadAtTicks { get; set; }

    public static NotificationRow From(UserNotification n) => new()
    {
        Id = n.Id,
        TenantId = n.TenantId,
        UserId = n.UserId,
        Severity = n.Severity,
        Title = n.Title,
        Message = n.Message,
        Category = n.Category,
        CreatedAtTicks = n.CreatedAt.UtcTicks,
        ReadAtTicks = n.ReadAt?.UtcTicks,
    };

    public UserNotification ToNotification() => new()
    {
        Id = Id,
        TenantId = TenantId,
        UserId = UserId,
        Severity = Severity,
        Title = Title,
        Message = Message,
        Category = Category,
        CreatedAt = new DateTimeOffset(CreatedAtTicks, TimeSpan.Zero),
        ReadAt = ReadAtTicks is { } read ? new DateTimeOffset(read, TimeSpan.Zero) : null,
    };
}

/// <summary>A channel choice row.</summary>
public sealed class NotificationPreferenceRow
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Category { get; set; } = "";

    public NotificationChannel Channel { get; set; }

    public bool Enabled { get; set; }
}
