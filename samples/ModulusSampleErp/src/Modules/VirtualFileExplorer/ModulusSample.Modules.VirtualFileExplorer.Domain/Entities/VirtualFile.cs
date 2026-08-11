using ModulusSample.Modules.VirtualFileExplorer.Domain.Events;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;

public sealed class VirtualFile : AggregateRoot, IAuditableEntity
{
    public new VirtualFileId Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string StoragePath { get; private set; } = default!;
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public VirtualFolderId FolderId { get; private set; }
    public Guid TenantId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    private VirtualFile() { }

    private VirtualFile(
        VirtualFileId id,
        string name,
        string storagePath,
        string? contentType,
        long sizeBytes,
        VirtualFolderId folderId,
        Guid tenantId,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        Name = name;
        StoragePath = storagePath;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        FolderId = folderId;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = createdBy;

        Raise(new VirtualFileUploadedDomainEvent(id, name, folderId, sizeBytes, tenantId));
    }

    public static Result<VirtualFile> Create(
        VirtualFileId id,
        string name,
        string storagePath,
        string? contentType,
        long sizeBytes,
        VirtualFolderId folderId,
        Guid tenantId,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<VirtualFile>(Error.Validation("VirtualFile.EmptyName", "File name cannot be empty"));
        }

        if (name.Length > 255)
        {
            return Result.Failure<VirtualFile>(Error.Validation("VirtualFile.TooLongName", "File name cannot exceed 255 characters"));
        }

        if (sizeBytes < 0)
        {
            return Result.Failure<VirtualFile>(Error.Validation("VirtualFile.InvalidSize", "File size cannot be negative"));
        }

        return Result.Success(new VirtualFile(id, name.Trim(), storagePath, contentType, sizeBytes, folderId, tenantId, createdBy));
    }

    public Result Rename(string newName, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result.Failure(Error.Validation("VirtualFile.EmptyName", "File name cannot be empty"));
        }

        if (newName.Length > 255)
        {
            return Result.Failure(Error.Validation("VirtualFile.TooLongName", "File name cannot exceed 255 characters"));
        }

        if (Name == newName.Trim())
        {
            return Result.Success();
        }

        Name = newName.Trim();
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        return Result.Success();
    }

    public void Delete(string deletedBy)
    {
        Raise(new VirtualFileDeletedDomainEvent(Id, Name, FolderId, TenantId));
    }

    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    DateTime IAuditableEntity.CreatedAt { get => CreatedAt; set => CreatedAt = value; }
    string? IAuditableEntity.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditableEntity.UpdatedAt { get => LastModifiedAt; set { if (value.HasValue) LastModifiedAt = value.Value; } }
    string? IAuditableEntity.UpdatedBy { get => LastModifiedBy; set => LastModifiedBy = value; }
}
