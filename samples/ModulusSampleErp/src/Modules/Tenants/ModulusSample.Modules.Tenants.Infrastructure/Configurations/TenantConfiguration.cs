using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.Tenants.Domain.Entities;
using ModulusSample.Modules.Tenants.Domain.ValueObjects;

namespace ModulusSample.Modules.Tenants.Infrastructure.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Id).HasConversion(
            id => id.Value,
            value => new TenantId(value));

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Subdomain)
            .HasConversion(
                subdomain => subdomain.Value,
                value => Subdomain.FromString(value))
            .IsRequired();

        builder.HasIndex(t => t.Subdomain).IsUnique();
        builder.HasIndex(t => t.Name);

        builder.Property(t => t.DatabaseConnectionString)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .IsRequired();

        builder.Property(t => t.Features)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.Settings)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired();

        builder.Property(t => t.CreatedBy)
            .HasMaxLength(200);

        builder.Property(t => t.LastModifiedAtUtc);

        builder.Property(t => t.LastModifiedBy)
            .HasMaxLength(200);

        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.DeletedAtUtc);

        builder.Property(t => t.DeletedBy)
            .HasMaxLength(200);

        builder.Property(t => t.Version)
            .IsConcurrencyToken()
            .IsRequired()
            .HasDefaultValue(1);

        builder.Ignore(t => t.DomainEvents);
    }
}