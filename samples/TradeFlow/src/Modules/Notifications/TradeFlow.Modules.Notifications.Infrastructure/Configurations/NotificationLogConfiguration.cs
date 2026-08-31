using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;

namespace TradeFlow.Modules.Notifications.Infrastructure.Configurations;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Id).HasConversion(
            id => id.Value,
            value => NotificationLogId.From(value));

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.NotificationId);
        builder.Property(l => l.EventKey).HasMaxLength(200).IsRequired();
        builder.Property(l => l.RecipientUserId).IsRequired();
        builder.Property(l => l.Channel).IsRequired();
        builder.Property(l => l.Status).IsRequired();
        builder.Property(l => l.ProviderMessageId).HasMaxLength(500);
        builder.Property(l => l.ProviderResponse);
        builder.Property(l => l.ErrorMessage);
        builder.Property(l => l.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(l => l.NextRetryAtUtc);
        builder.Property(l => l.SentAtUtc);
        builder.Property(l => l.DeliveredAtUtc);
        builder.Property(l => l.ReadAtUtc);
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc).IsRequired();

        builder.HasIndex(l => l.TenantId);
        builder.HasIndex(l => new { l.TenantId, l.Status });
        builder.HasIndex(l => l.EventKey);
        builder.HasIndex(l => l.NotificationId);

        builder.Ignore(l => l.DomainEvents);
    }
}
