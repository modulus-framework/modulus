using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradeFlow.Modules.Identity.Infrastructure.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", Schemas.Users);

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(
                roleId => roleId.Value,
                value => RoleId.Create(value));

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(r => r.IsSystem)
            .HasColumnName("is_system")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(r => r.LastReviewedAtUtc)
            .HasColumnName("last_reviewed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasDatabaseName("ix_roles_name");

        builder.HasIndex(r => r.IsSystem)
            .HasDatabaseName("ix_roles_is_system");

        builder.OwnsMany<RolePermission>("_permissions", rpBuilder =>
        {
            rpBuilder.ToTable("role_permissions", Schemas.Users);
            rpBuilder.WithOwner().HasForeignKey(x => x.RoleId);
            rpBuilder.HasKey(x => x.Id);
            rpBuilder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
            rpBuilder.Property(x => x.RoleId).HasColumnName("role_id")
                .HasConversion(id => id.Value, value => RoleId.Create(value)).IsRequired();
            rpBuilder.Property(x => x.PermissionId).HasColumnName("permission_id").HasMaxLength(100)
                .HasConversion(id => id.Value, value => PermissionId.Create(value)).IsRequired();
            rpBuilder.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id")
                .HasConversion(id => id.Value, value => UserId.Create(value)).IsRequired();
            rpBuilder.Property(x => x.GrantedAtUtc).HasColumnName("granted_at_utc")
                .HasColumnType("timestamp with time zone").IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            rpBuilder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);
            rpBuilder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc")
                .HasColumnType("timestamp with time zone");
            rpBuilder.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id")
                .HasConversion(id => id!.Value, value => UserId.Create(value));
            rpBuilder.HasIndex(x => new { x.RoleId, x.PermissionId })
                .HasDatabaseName("ix_role_permissions_role_id_permission_id");
            rpBuilder.HasIndex(x => x.RoleId).HasDatabaseName("ix_role_permissions_role_id");
            rpBuilder.HasIndex(x => x.PermissionId).HasDatabaseName("ix_role_permissions_permission_id");
            rpBuilder.HasIndex(x => x.IsActive).HasDatabaseName("ix_role_permissions_is_active");
            rpBuilder.HasIndex(x => new { x.RoleId, x.IsActive })
                .HasDatabaseName("ix_role_permissions_role_id_is_active");
            rpBuilder.HasIndex(x => x.GrantedByUserId).HasDatabaseName("ix_role_permissions_granted_by_user_id");
        });
        builder.Ignore(r => r.Permissions);

        builder.Ignore(r => r.DomainEvents);
    }
}
