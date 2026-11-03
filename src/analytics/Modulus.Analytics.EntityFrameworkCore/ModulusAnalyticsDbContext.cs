namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The analytics delivery store. Derive the app's context from it (so its migrations live in the app) and register that with
/// <c>AddModulusAnalyticsStore&lt;TContext&gt;()</c>. It has no tenant query filter on purpose: the worker serves every tenant.
/// </summary>
public class ModulusAnalyticsDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The context options.</param>
    protected ModulusAnalyticsDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Queued records of every tenant.</summary>
    public DbSet<AnalyticsDelivery> AnalyticsDeliveries => Set<AnalyticsDelivery>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        ConfigureAnalytics(modelBuilder);
    }

    /// <summary>Maps the analytics entities; call it from another context's <c>OnModelCreating</c> to host them there.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureAnalytics(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AnalyticsDelivery>(b =>
        {
            b.ToTable("analytics_deliveries");
            b.HasKey(d => d.Id);
            b.Ignore(d => d.Status);
            b.Property(d => d.Dataset).HasMaxLength(128).IsRequired();
            b.Property(d => d.Payload).IsRequired();
            b.Property(d => d.LockedBy).HasMaxLength(64);
            b.Property(d => d.LastError).HasMaxLength(2048);
            b.HasIndex(d => new { d.TenantId, d.Dataset, d.EventId }).IsUnique();
            b.HasIndex(d => new { d.DeliveredAt, d.DeadLetteredAt, d.NextAttemptAt });
            b.HasIndex(d => new { d.TenantId, d.CreatedAt });
        });
    }
}
