namespace ModulusSample.Modules.Media.Application.Dtos;

using ModulusSample.Modules.Media.Domain.Enums;

public sealed class UploadMediaFileResponse
{
    public Guid MediaFileId { get; set; }
    public string FileName { get; set; }
    public string StoragePath { get; set; }
    public string FileUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public MediaFileType FileType { get; set; }
    public long FileSizeBytes { get; set; }
}
