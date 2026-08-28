using ProcureFlow.Modules.VirtualFileExplorer.Domain.Events;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ProcureFlow.Modules.VirtualFileExplorer.Domain.Entities;

public sealed class VirtualFolder : AggregateRoot, IAuditableEntity
{
    public new VirtualFolderId Id { get; private set; }
    public string Name { get; private set; } = default!;
    public VirtualFolderId? ParentFolderId { get; private set; }
    public Guid TenantId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    private VirtualFolder() { }

    private VirtualFolder(
        VirtualFolderId id,
        string name,
        VirtualFolderId? parentFolderId,
        Guid tenantId,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        Name = name;
        ParentFolderId = parentFolderId;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = createdBy;

        Raise(new VirtualFolderCreatedDomainEvent(id, name, parentFolderId, tenantId));
    }

    public static Result<VirtualFolder> Create(
        VirtualFolderId id,
        string name,
        VirtualFolderId? parentFolderId,
        Guid tenantId,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<VirtualFolder>(Error.Validation("VirtualFolder.EmptyName", "Folder name cannot be empty"));
        }

        if (name.Length > 255)
        {
            return Result.Failure<VirtualFolder>(Error.Validation("VirtualFolder.TooLongName", "Folder name cannot exceed 255 characters"));
        }

        return Result.Success(new VirtualFolder(id, name.Trim(), parentFolderId, tenantId, createdBy));
    }

    public Result Rename(string newName, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result.Failure(Error.Validation("VirtualFolder.EmptyName", "Folder name cannot be empty"));
        }

        if (newName.Length > 255)
        {
            return Result.Failure(Error.Validation("VirtualFolder.TooLongName", "Folder name cannot exceed 255 characters"));
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
        Raise(new VirtualFolderDeletedDomainEvent(Id, Name, TenantId));
    }

    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    DateTime IAuditableEntity.CreatedAt { get => CreatedAt; set => CreatedAt = value; }
    string? IAuditableEntity.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditableEntity.UpdatedAt { get => LastModifiedAt; set { if (value.HasValue) LastModifiedAt = value.Value; } }
    string? IAuditableEntity.UpdatedBy { get => LastModifiedBy; set => LastModifiedBy = value; }
}
