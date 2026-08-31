using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;

namespace TradeFlow.Modules.Notifications.Infrastructure.Configurations;

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Id).HasConversion(
            id => id.Value,
            value => NotificationTemplateId.From(value));

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.TemplateKey).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Channel).IsRequired();
        builder.Property(t => t.Locale).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(500);
        builder.Property(t => t.Body).IsRequired();
        builder.Property(t => t.VariablesJsonSchema);
        builder.Property(t => t.Version).IsRequired().HasDefaultValue(1);
        builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        builder.HasIndex(t => new { t.TenantId, t.TemplateKey, t.Channel, t.Locale }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.TemplateKey });

        builder.Ignore(t => t.DomainEvents);
    }
}
