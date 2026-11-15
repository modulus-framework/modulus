using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

public static class MessagingOutboxModelBuilderExtensions
{
    /// <summary>Maps <see cref="OutboxRecord"/> into the producing service's DbContext. Call it from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder ApplyModulusMessagingOutbox(this ModelBuilder modelBuilder, string tableName = "messaging_outbox")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<OutboxRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(r => r.MessageId);
            b.Property(r => r.Endpoint).HasMaxLength(256).IsRequired();
            b.Property(r => r.ConsumerKey).HasMaxLength(512);
            b.Property(r => r.MessageType).HasMaxLength(1024).IsRequired();
            b.Property(r => r.Body).IsRequired();
            b.Property(r => r.Headers).IsRequired();
            b.HasIndex(r => new { r.Parked, r.LockedUntil, r.EnqueuedAt });
        });

        return modelBuilder;
    }
}
