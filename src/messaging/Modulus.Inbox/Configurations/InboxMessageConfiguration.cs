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

        // Composite PK: (Id, HandlerName). Two handlers subscribed to the same
        // integration event claim independent rows instead of racing over one
        // shared-by-EventId row (the row a fan-out handler claims first would
        // otherwise mark the event Processed for every OTHER handler too).
        // HandlerName defaults to "" for rows written before this column
        // existed ("legacy" rows) — see InboxMessage.HandlerName.
        b.Property(x => x.HandlerName).HasMaxLength(500).IsRequired().HasDefaultValue(string.Empty);
        b.HasKey(x => new { x.Id, x.HandlerName });

        b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
        b.Property(x => x.ModuleName).HasMaxLength(100);
        // Unique claim semantics come from the PK itself (EventId + HandlerName): a
        // concurrent duplicate INSERT races and the loser defers.
        b.Property(x => x.Status)
         .HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.Status, x.ReceivedAt });
    }
}
