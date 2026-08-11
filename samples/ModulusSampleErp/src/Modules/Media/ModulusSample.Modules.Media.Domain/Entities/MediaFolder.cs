namespace ModulusSample.Modules.Media.Domain.Entities;

using ModulusSample.Shared.Domain;

public sealed class MediaFolder : AggregateRoot
{
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? ParentFolderId { get; private set; }
    public string Path { get; private set; }
    public int FileCount { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private MediaFolder()
    {
    }

    public MediaFolder(
        Guid id,
        string name,
        string? description,
        Guid? parentFolderId,
        string path,
        Guid? tenantId = null,
        Guid? createdBy = null)
    {
        Id = id;
        Name = name;
        Description = description;
        ParentFolderId = parentFolderId;
        Path = path;
        TenantId = tenantId;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateInfo(string name, string? description = null)
    {
        Name = name;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementFileCount()
    {
        FileCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecrementFileCount()
    {
        if (FileCount > 0)
        {
            FileCount--;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void MoveToFolder(Guid? parentFolderId, string newPath)
    {
        ParentFolderId = parentFolderId;
        Path = newPath;
        UpdatedAt = DateTime.UtcNow;
    }
}
