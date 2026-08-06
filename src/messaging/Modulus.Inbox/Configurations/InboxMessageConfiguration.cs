namespace Modulus.Inbox.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulus.Inbox.Abstractions;

public sealed class InboxMessageConfiguration
    : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("inbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
        b.Property(x => x.ModuleName).HasMaxLength(100);
        b.Property(x => x.Status)
         .HasConversion<string>().HasMaxLength(20);
        // Unique index prevents concurrent duplicate processing
        b.HasIndex(x => x.Id).IsUnique();
        b.HasIndex(x => new { x.Status, x.ReceivedAt });
    }
}
