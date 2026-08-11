using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.Features.Domain.Entities;
using ModulusSample.Modules.Features.Domain.ValueObjects;
using ModulusSample.Modules.Features.Infrastructure.Database;

namespace ModulusSample.Modules.Features.Infrastructure.Configurations;

public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags", Schemas.Features);

        builder.Property(f => f.Id)
            .HasConversion(
                id => id.Value,
                value => FeatureFlagId.From(value))
            .IsRequired();

        builder.Property(f => f.Key)
            .HasConversion(
                key => key.Value,
                value => FeatureKey.FromString(value))
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(f => f.Description)
            .HasMaxLength(500);

        builder.Property(f => f.IsEnabled)
            .IsRequired();

        builder.Property(f => f.TenantId)
            .IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.Key }).IsUnique();

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.CreatedBy)
            .HasMaxLength(36);

        builder.Property(f => f.UpdatedAt)
            .IsRequired();

        builder.Property(f => f.UpdatedBy)
            .HasMaxLength(36);

        builder.Property(f => f.Version)
            .IsConcurrencyToken()
            .IsRequired()
            .HasDefaultValue(1);

        builder.Ignore(f => f.DomainEvents);
    }
}
