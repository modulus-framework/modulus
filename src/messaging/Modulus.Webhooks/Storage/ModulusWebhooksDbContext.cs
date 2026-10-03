namespace Modulus.Webhooks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// The webhook store: subscriptions and deliveries. Derive the app's context from it (so its migrations live in the
/// app) and register that with <c>AddModulusWebhooksStore&lt;TContext&gt;()</c>. It has no tenant query filter on
/// purpose: the delivery worker serves every tenant, and the management API filters by the current tenant itself.
/// </summary>
public class ModulusWebhooksDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    protected ModulusWebhooksDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>Subscriptions of every tenant.</summary>
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    /// <summary>Deliveries of every tenant.</summary>
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        ConfigureWebhooks(modelBuilder);
    }

    /// <summary>Maps the webhook entities; call it from another context's <c>OnModelCreating</c> to host them there.</summary>
    public static void ConfigureWebhooks(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WebhookSubscription>(b =>
        {
            b.ToTable("webhook_subscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.Url).HasMaxLength(2048).IsRequired();
            b.Property(s => s.Description).HasMaxLength(500);
            b.Property(s => s.EventTypes)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    new ValueComparer<List<string>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
                        v => v.ToList()))
                .HasMaxLength(4000)
                .IsRequired();
            b.Property(s => s.ProtectedSecret).HasMaxLength(2048).IsRequired();
            b.Property(s => s.ProtectedPreviousSecret).HasMaxLength(2048);
            b.Property(s => s.DisabledReason).HasMaxLength(500);
            b.HasIndex(s => s.TenantId);
        });

        modelBuilder.Entity<WebhookDelivery>(b =>
        {
            b.ToTable("webhook_deliveries");
            b.HasKey(d => d.Id);
            b.Ignore(d => d.Status);
            b.Ignore(d => d.MessageId);
            b.Property(d => d.EventType).HasMaxLength(256).IsRequired();
            b.Property(d => d.Payload).IsRequired();
            b.Property(d => d.CorrelationId).HasMaxLength(128);
            b.Property(d => d.LockedBy).HasMaxLength(64);
            b.Property(d => d.LastError).HasMaxLength(2048);
            b.HasOne<WebhookSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => new { d.SubscriptionId, d.EventId }).IsUnique();
            b.HasIndex(d => new { d.DeliveredAt, d.DeadLetteredAt, d.NextAttemptAt });
            b.HasIndex(d => new { d.TenantId, d.CreatedAt });
        });
    }
}
