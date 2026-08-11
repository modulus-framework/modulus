using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.Notifications.Domain.Entities;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;

namespace ModulusSample.Modules.Notifications.Infrastructure.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Id).HasConversion(
            id => id.Value,
            value => NotificationId.From(value));

        builder.Property(n => n.RecipientUserId)
            .IsRequired();

        builder.Property(n => n.Title)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(n => n.Message)
            .IsRequired();

        builder.Property(n => n.Type)
            .IsRequired();

        builder.Property(n => n.IsRead)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(n => n.ReadAtUtc);

        builder.Property(n => n.TenantId)
            .IsRequired();

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.Property(n => n.CreatedBy)
            .HasMaxLength(256);

        builder.Property(n => n.LastModifiedAt)
            .IsRequired();

        builder.Property(n => n.LastModifiedBy)
            .HasMaxLength(256);

        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });
        builder.HasIndex(n => n.TenantId);

        builder.Ignore(n => n.DomainEvents);
    }
}
