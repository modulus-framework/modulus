using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;

namespace ProcureFlow.Modules.Notifications.Infrastructure.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Id).HasConversion(
            id => id.Value,
            value => NotificationPreferenceId.From(value));

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.EventCategory).HasMaxLength(200).IsRequired();
        builder.Property(p => p.EnabledChannels).IsRequired();
        builder.Property(p => p.IsMandatory).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.QuietHoursStart).HasMaxLength(5);
        builder.Property(p => p.QuietHoursEnd).HasMaxLength(5);
        builder.Property(p => p.TimeZoneId).HasMaxLength(50);
        builder.Property(p => p.DigestFrequency).HasMaxLength(20);
        builder.Property(p => p.Locale).HasMaxLength(10);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.UserId, p.EventCategory }).IsUnique();
        builder.HasIndex(p => new { p.UserId, p.TenantId });

        builder.Ignore(p => p.DomainEvents);
    }
}
