using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradeFlow.Modules.Identity.Infrastructure.Configurations;

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens", Schemas.Users);

        builder.HasKey(dt => dt.Id);

        builder.Property(dt => dt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(dt => dt.Token)
            .HasColumnName("token")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(dt => dt.DeviceType)
            .HasColumnName("device_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(dt => dt.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(dt => dt.LastUsedAt)
            .HasColumnName("last_used_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(dt => dt.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(dt => dt.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(dt => dt.Token)
            .IsUnique()
            .HasDatabaseName("ix_device_tokens_token");

        builder.HasIndex(dt => dt.UserId)
            .HasDatabaseName("ix_device_tokens_user_id");

        builder.HasIndex(dt => new { dt.UserId, dt.DeviceType })
            .HasDatabaseName("ix_device_tokens_user_id_device_type");

        builder.HasIndex(dt => dt.IsActive)
            .HasDatabaseName("ix_device_tokens_is_active");

        builder.HasIndex(dt => dt.ExpiresAt)
            .HasDatabaseName("ix_device_tokens_expires_at");
    }
}
