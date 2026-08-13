namespace ModulusSample.Modules.Media.Application.Folders.Dtos;

using ModulusSample.Modules.Media.Domain.Entities;

/// <summary>
/// Maps media folder aggregates to DTOs shared across the query handlers.
/// </summary>
public static class MediaFolderDtoMapper
{
    public static MediaFolderDto ToDto(MediaFolder folder, int childFolderCount, string? parentName = null)
    {
        return new MediaFolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            Description = folder.Description,
            ParentFolderId = folder.ParentFolderId,
            ParentFolderName = parentName,
            Path = folder.Path,
            FileCount = folder.FileCount,
            ChildFolderCount = childFolderCount,
            TenantId = folder.TenantId,
            CreatedBy = folder.CreatedBy,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt
        };
    }
}