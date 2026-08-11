namespace ModulusSample.Modules.Media.Application.Dtos;

using ModulusSample.Modules.Media.Domain.Enums;

public sealed class MediaFileDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string OriginalFileName { get; set; }
    public string Extension { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string FileSizeFormatted { get; set; }
    public string StoragePath { get; set; }
    public MediaFileType FileType { get; set; }
    public MediaFileStatus Status { get; set; }
    public string? AltText { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? FileUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
