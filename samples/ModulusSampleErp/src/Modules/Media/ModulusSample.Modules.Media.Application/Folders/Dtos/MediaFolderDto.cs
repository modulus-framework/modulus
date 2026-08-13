namespace ModulusSample.Modules.Media.Application.Folders.Dtos;

public sealed class MediaFolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string? ParentFolderName { get; set; }
    public string Path { get; set; }
    public int FileCount { get; set; }
    public int ChildFolderCount { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
