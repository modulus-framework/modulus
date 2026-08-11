namespace ModulusSample.Modules.Media.Domain.Entities;

using ModulusSample.Modules.Media.Domain.Enums;
using ModulusSample.Modules.Media.Domain.Events;
using ModulusSample.Modules.Media.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

public sealed class MediaFile : AggregateRoot
{
    public string FileName { get; private set; }
    public string OriginalFileName { get; private set; }
    public string Extension { get; private set; }
    public string ContentType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string StoragePath { get; private set; }
    public MediaFileType FileType { get; private set; }
    public MediaFileStatus Status { get; private set; }
    public string? AltText { get; private set; }
    public string? Description { get; private set; }
    public string? ThumbnailPath { get; private set; }
    public Dimensions? Dimensions { get; private set; }
    public int Width => Dimensions?.Width ?? 0;
    public int Height => Dimensions?.Height ?? 0;
    public Guid? FolderId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private MediaFile()
    {
    }

    public MediaFile(
        Guid id,
        string fileName,
        string originalFileName,
        string extension,
        string contentType,
        long fileSizeBytes,
        string storagePath,
        MediaFileType fileType,
        Guid? folderId = null,
        Guid? tenantId = null,
        Guid? createdBy = null)
    {
        Id = id;
        FileName = fileName;
        OriginalFileName = originalFileName;
        Extension = extension;
        ContentType = contentType;
        FileSizeBytes = fileSizeBytes;
        StoragePath = storagePath;
        FileType = fileType;
        Status = MediaFileStatus.Uploaded;
        FolderId = folderId;
        TenantId = tenantId;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;

        Raise(new MediaFileUploadedEvent(id, fileName, storagePath));
    }

    public void MarkAsProcessed(string? thumbnailPath = null, Dimensions? dimensions = null)
    {
        Status = MediaFileStatus.Processed;
        ThumbnailPath = thumbnailPath;
        Dimensions = dimensions;
        UpdatedAt = DateTime.UtcNow;

        Raise(new MediaFileProcessedEvent(Id, FileName, StoragePath));
    }

    public void MarkAsFailed(string reason)
    {
        Status = MediaFileStatus.Failed;
        UpdatedAt = DateTime.UtcNow;

        Raise(new MediaFileUploadFailedEvent(Id, FileName, reason));
    }

    public void UpdateMetadata(string? altText = null, string? description = null)
    {
        AltText = altText;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MoveToFolder(Guid? folderId)
    {
        FolderId = folderId;
        UpdatedAt = DateTime.UtcNow;
    }
}
