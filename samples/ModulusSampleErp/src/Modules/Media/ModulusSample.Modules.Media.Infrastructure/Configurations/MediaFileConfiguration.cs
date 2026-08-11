using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Infrastructure.Database;

namespace ModulusSample.Modules.Media.Infrastructure.Configurations;

public sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files", Schemas.Media);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.OriginalFileName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Extension)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.FileSizeBytes)
            .IsRequired();

        builder.Property(e => e.StoragePath)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.FileType)
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.AltText)
            .HasMaxLength(512);

        builder.Property(e => e.Description)
            .HasMaxLength(2048);

        builder.Property(e => e.ThumbnailPath)
            .HasMaxLength(512);

        builder.Property(e => e.FolderId);

        builder.Property(e => e.TenantId);

        builder.Property(e => e.CreatedBy);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);

        builder.HasIndex(e => e.StoragePath)
            .IsUnique();

        builder.HasIndex(e => e.FolderId);
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.FileType);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAt);

        builder.OwnsOne(e => e.Dimensions, owned =>
        {
            owned.Property(d => d.Width)
                .HasColumnName("width");

            owned.Property(d => d.Height)
                .HasColumnName("height");
        });

        builder.Ignore(e => e.Width);
        builder.Ignore(e => e.Height);
        builder.Ignore(e => e.DomainEvents);
    }
}