using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Modules.Identity.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProcureFlow.Modules.Identity.Infrastructure.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", Schemas.Users);

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasMaxLength(100)
            .HasConversion(
                permissionId => permissionId.Value,
                value => PermissionId.Create(value));

        builder.Property(p => p.Code)
            .HasColumnName("code")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.Category)
            .HasColumnName("category")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("ix_permissions_code");

        builder.HasIndex(p => p.Category)
            .HasDatabaseName("ix_permissions_category");

        builder.HasIndex(p => p.IsActive)
            .HasDatabaseName("ix_permissions_is_active");

        builder.HasIndex(p => new { p.Category, p.IsActive })
            .HasDatabaseName("ix_permissions_category_is_active");
    }
}
