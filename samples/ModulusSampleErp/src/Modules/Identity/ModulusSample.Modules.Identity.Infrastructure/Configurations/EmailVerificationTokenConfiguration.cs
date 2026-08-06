using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModulusSample.Modules.Identity.Infrastructure.Configurations;

internal sealed class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens", Schemas.Users);

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.IsUsed)
            .HasColumnName("is_used")
            .IsRequired();

        builder.Property(t => t.UsedAtUtc)
            .HasColumnName("used_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(t => t.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(t => new { t.UserId, t.IsUsed, t.CreatedAtUtc })
            .HasDatabaseName("ix_email_verification_tokens_user_created");

        builder.HasIndex(t => t.TokenHash)
            .HasDatabaseName("ix_email_verification_tokens_hash")
            .IsUnique();

        builder.HasIndex(t => t.ExpiresAtUtc)
            .HasDatabaseName("ix_email_verification_tokens_expires");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_email_verification_tokens_users")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
