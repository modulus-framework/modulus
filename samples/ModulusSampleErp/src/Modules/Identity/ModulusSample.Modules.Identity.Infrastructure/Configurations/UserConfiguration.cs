using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Enums;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhoneNumber = ModulusSample.Shared.Domain.ValueObjects.PhoneNumber;

namespace ModulusSample.Modules.Identity.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", Schemas.Users);

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion(
                userId => userId.Value,
                value => new UserId(value))
            .ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .HasConversion(
                email => email.Value,
                value => Email.Create(value).Value)
            .IsRequired();

        builder.Property(u => u.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(100)
            .HasConversion(
                userName => userName.Value,
                value => UserName.Create(value))
            .IsRequired();

        builder.Property(u => u.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20)
            .HasConversion(
                phoneNumber => phoneNumber == null ? null : phoneNumber.Value,
                value => value == null ? null : PhoneNumber.Create(value).Value);

        builder.Property(u => u.ProfileImageUrl)
            .HasColumnName("profile_image_url")
            .HasMaxLength(500);

        builder.Property(u => u.UserType)
            .HasColumnName("user_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500);

        builder.Property(u => u.EmailConfirmed)
            .HasColumnName("email_confirmed")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.PhoneNumberConfirmed)
            .HasColumnName("phone_number_confirmed")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(u => u.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(u => u.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(u => u.LastActivityAtUtc)
            .HasColumnName("last_activity_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(u => u.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(u => u.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(100);

        builder.Property(u => u.LastModifiedBy)
            .HasColumnName("last_modified_by")
            .HasMaxLength(100);

        builder.Property(u => u.LastModifiedAtUtc)
            .HasColumnName("last_modified_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email");

        builder.HasIndex(u => u.UserName)
            .IsUnique()
            .HasDatabaseName("ix_users_user_name");

        builder.HasIndex(u => u.PhoneNumber)
            .HasDatabaseName("ix_users_phone_number");

        builder.HasIndex(u => u.Status)
            .HasDatabaseName("ix_users_status");

        builder.HasIndex(u => u.UserType)
            .HasDatabaseName("ix_users_user_type");

        builder.HasIndex(u => u.IsDeleted)
            .HasDatabaseName("ix_users_is_deleted");

        builder.HasIndex(u => u.LastActivityAtUtc)
            .HasDatabaseName("ix_users_last_activity_at_utc");

        builder.HasIndex(u => new { u.Email, u.Status })
            .HasDatabaseName("ix_users_email_status");

        builder.HasIndex(u => new { u.UserType, u.Status })
            .HasDatabaseName("ix_users_user_type_status");

        builder.HasIndex(u => new { u.LastActivityAtUtc, u.Status })
            .HasDatabaseName("ix_users_last_activity_at_utc_status");

        builder.OwnsMany<UserRole>("_userRoles", urBuilder =>
        {
            urBuilder.ToTable("user_roles", Schemas.Users);
            urBuilder.WithOwner().HasForeignKey(x => x.UserId);
            urBuilder.HasKey(x => x.Id);
            urBuilder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().IsRequired();
            urBuilder.Property(x => x.UserId).HasColumnName("user_id").HasConversion(id => id.Value, value => new UserId(value)).IsRequired();
            urBuilder.Property(x => x.RoleId).HasColumnName("role_id").HasConversion(id => id.Value, value => RoleId.Create(value)).IsRequired();
            urBuilder.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").HasColumnType("timestamp with time zone").HasDefaultValueSql("(NOW() AT TIME ZONE 'UTC')").IsRequired();
            urBuilder.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique().HasDatabaseName("ix_user_roles_user_id_role_id");
            urBuilder.HasIndex(x => x.UserId).HasDatabaseName("ix_user_roles_user_id");
            urBuilder.HasIndex(x => x.RoleId).HasDatabaseName("ix_user_roles_role_id");
            urBuilder.HasIndex(x => x.AssignedAtUtc).HasDatabaseName("ix_user_roles_assigned_at_utc");
        });
        builder.Ignore(u => u.UserRoles);

        builder.Ignore(u => u.FullName);
        builder.Ignore(u => u.IsSystemAdministrator);
        builder.Ignore(u => u.DomainEvents);

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
