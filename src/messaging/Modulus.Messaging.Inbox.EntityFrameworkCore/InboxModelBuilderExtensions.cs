using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

public static class MessagingInboxModelBuilderExtensions
{
    /// <summary>Maps <see cref="InboxRecord"/> into the consuming service's DbContext. Call it from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder ApplyModulusMessagingInbox(this ModelBuilder modelBuilder, string tableName = "messaging_inbox")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<InboxRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(r => new { r.MessageId, r.ConsumerKey });
            b.Property(r => r.ConsumerKey).HasMaxLength(512);
            b.HasIndex(r => r.ReceivedAt);
        });

        return modelBuilder;
    }
}
