using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;

namespace ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Configurations;

public sealed class VirtualFileConfiguration : IEntityTypeConfiguration<VirtualFile>
{
    public void Configure(EntityTypeBuilder<VirtualFile> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Id).HasConversion(
            id => id.Value,
            value => VirtualFileId.From(value));

        builder.Property(f => f.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.StoragePath)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(f => f.ContentType)
            .HasMaxLength(256);

        builder.Property(f => f.SizeBytes)
            .IsRequired();

        builder.Property(f => f.FolderId)
            .HasConversion(
                id => id.Value,
                value => VirtualFolderId.From(value))
            .IsRequired();

        builder.Property(f => f.TenantId)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.CreatedBy)
            .HasMaxLength(256);

        builder.Property(f => f.LastModifiedAt)
            .IsRequired();

        builder.Property(f => f.LastModifiedBy)
            .HasMaxLength(256);

        builder.HasIndex(f => new { f.FolderId, f.Name })
            .IsUnique();

        builder.HasIndex(f => f.TenantId);
        builder.HasIndex(f => f.FolderId);

        builder.Ignore(f => f.DomainEvents);
    }
}