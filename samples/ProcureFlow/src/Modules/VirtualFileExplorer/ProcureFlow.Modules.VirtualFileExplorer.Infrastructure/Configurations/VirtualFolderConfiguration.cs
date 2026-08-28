using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.Entities;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

namespace ProcureFlow.Modules.VirtualFileExplorer.Infrastructure.Configurations;

public sealed class VirtualFolderConfiguration : IEntityTypeConfiguration<VirtualFolder>
{
    public void Configure(EntityTypeBuilder<VirtualFolder> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Id).HasConversion(
            id => id.Value,
            value => VirtualFolderId.From(value));

        builder.Property(f => f.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.ParentFolderId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? VirtualFolderId.From(value.Value) : null);

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

        builder.HasIndex(f => new { f.TenantId, f.ParentFolderId, f.Name })
            .IsUnique();

        builder.HasIndex(f => f.TenantId);
        builder.HasIndex(f => f.ParentFolderId);

        builder.Ignore(f => f.DomainEvents);
    }
}
