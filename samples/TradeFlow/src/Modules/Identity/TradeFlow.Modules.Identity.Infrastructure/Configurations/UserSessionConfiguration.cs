using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradeFlow.Modules.Identity.Infrastructure.Configurations;

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions", Schemas.Users);

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .HasConversion(
                userId => userId.Value,
                value => new UserId(value))
            .IsRequired();

        builder.Property(s => s.ExternalSessionId)
            .HasColumnName("keycloak_session_state")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.AccessTokenJti)
            .HasColumnName("access_token_jti")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.RefreshTokenJti)
            .HasColumnName("refresh_token_jti")
            .HasMaxLength(255);

        builder.Property(s => s.DeviceInfo)
            .HasColumnName("device_info")
            .HasConversion(
                deviceInfo => deviceInfo.ToJson(),
                json => DeviceInfo.FromJson(json));

        builder.Property(s => s.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(s => s.LoginTimeUtc)
            .HasColumnName("login_time_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.LastActivityTimeUtc)
            .HasColumnName("last_activity_time_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasMaxLength(255);

        builder.Property(s => s.IdTokenHash)
            .HasColumnName("id_token_hash")
            .HasMaxLength(255);

        builder.Property(s => s.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("ix_user_sessions_user_id");

        builder.HasIndex(s => s.ExternalSessionId)
            .HasDatabaseName("ix_user_sessions_keycloak_state");

        builder.HasIndex(s => s.IsRevoked)
            .HasDatabaseName("ix_user_sessions_is_revoked");

        builder.HasIndex(s => s.ExpiresAtUtc)
            .HasDatabaseName("ix_user_sessions_expires_at");

        builder.HasIndex(s => new { s.UserId, s.IsRevoked })
            .HasDatabaseName("ix_user_sessions_user_active")
            .HasFilter("is_revoked = FALSE");

        builder.HasIndex(s => s.ExternalSessionId)
            .HasDatabaseName("ix_user_sessions_unique_keycloak_state")
            .IsUnique()
            .HasFilter("is_revoked = FALSE");

        builder.HasOne<Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
