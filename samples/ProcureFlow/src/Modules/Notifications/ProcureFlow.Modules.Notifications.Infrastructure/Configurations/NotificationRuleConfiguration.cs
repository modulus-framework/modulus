using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;

namespace ProcureFlow.Modules.Notifications.Infrastructure.Configurations;

public sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Id).HasConversion(
            id => id.Value,
            value => NotificationRuleId.From(value));

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.EventKey).HasMaxLength(200).IsRequired();
        builder.Property(r => r.AudienceJson).IsRequired();
        builder.Property(r => r.Channels).IsRequired();
        builder.Property(r => r.Severity).IsRequired();
        builder.Property(r => r.TemplateKey).HasMaxLength(200);
        builder.Property(r => r.ThrottleJson);
        builder.Property(r => r.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.EventKey }).IsUnique();
        builder.HasIndex(r => r.EventKey);

        builder.Ignore(r => r.DomainEvents);
    }
}
