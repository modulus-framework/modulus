namespace ModulusSample.Modules.Media.Application.Dtos;

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

/// <summary>
/// Maps domain aggregates to DTOs shared across the query handlers.
/// </summary>
public static class MediaFileDtoMapper
{
    public static MediaFileDto ToDto(MediaFile mediaFile, string fileUrl, string? thumbnailUrl = null)
    {
        return new MediaFileDto
        {
            Id = mediaFile.Id,
            FileName = mediaFile.FileName,
            OriginalFileName = mediaFile.OriginalFileName,
            Extension = mediaFile.Extension,
            ContentType = mediaFile.ContentType,
            FileSizeBytes = mediaFile.FileSizeBytes,
            FileSizeFormatted = FormatFileSize(mediaFile.FileSizeBytes),
            StoragePath = mediaFile.StoragePath,
            FileType = mediaFile.FileType,
            Status = mediaFile.Status,
            AltText = mediaFile.AltText,
            Description = mediaFile.Description,
            ThumbnailUrl = thumbnailUrl,
            FileUrl = fileUrl,
            Width = mediaFile.Width,
            Height = mediaFile.Height,
            FolderId = mediaFile.FolderId,
            TenantId = mediaFile.TenantId,
            CreatedBy = mediaFile.CreatedBy,
            CreatedAt = mediaFile.CreatedAt,
            UpdatedAt = mediaFile.UpdatedAt
        };
    }

    public static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
