using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Infrastructure.Database;

namespace ModulusSample.Modules.Media.Infrastructure.Configurations;

public sealed class MediaFolderConfiguration : IEntityTypeConfiguration<MediaFolder>
{
    public void Configure(EntityTypeBuilder<MediaFolder> builder)
    {
        builder.ToTable("media_folders", Schemas.Media);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Description)
            .HasMaxLength(2048);

        builder.Property(e => e.ParentFolderId);

        builder.Property(e => e.Path)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.FileCount)
            .IsRequired();

        builder.Property(e => e.TenantId);

        builder.Property(e => e.CreatedBy);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);

        builder.HasIndex(e => e.Path)
            .IsUnique();

        builder.HasIndex(e => e.ParentFolderId);
        builder.HasIndex(e => e.TenantId);

        builder.Ignore(e => e.DomainEvents);
    }
}