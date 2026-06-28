namespace Modulus.Outbox.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulus.Outbox.Abstractions;

public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.ModuleName).HasMaxLength(100);
        // Supports the candidate query (unprocessed + lock-free + under budget).
        b.HasIndex(x => new { x.ProcessedAt, x.LockedUntil, x.RetryCount });
        b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
        b.HasIndex(x => x.TenantId);
    }
}