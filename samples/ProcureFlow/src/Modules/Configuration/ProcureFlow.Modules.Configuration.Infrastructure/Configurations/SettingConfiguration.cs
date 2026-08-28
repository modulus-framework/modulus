using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Infrastructure.Database;

namespace ProcureFlow.Modules.Configuration.Infrastructure.Configurations;

public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("settings", Schemas.Settings);

        builder.Property<Guid>("id")
            .IsRequired();

        builder.Property(s => s.Key)
            .HasConversion(
                key => key.Value,
                value => ProcureFlow.Modules.Configuration.Domain.ValueObjects.SettingKey.FromString(value))
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Value)
            .IsRequired();

        builder.Property(s => s.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.IsPublic)
            .IsRequired();

        builder.Property(s => s.TenantId)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.CreatedBy)
            .HasMaxLength(256);

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedBy)
            .HasMaxLength(256);

        builder.HasIndex(s => new { s.TenantId, s.Key })
            .IsUnique();

        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.Category);
        builder.HasIndex(s => s.IsPublic);

        builder.Ignore(s => s.Id);
        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.Version);
    }
}
