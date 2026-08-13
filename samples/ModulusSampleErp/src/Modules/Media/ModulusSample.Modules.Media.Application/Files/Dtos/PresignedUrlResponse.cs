namespace ModulusSample.Modules.Media.Application.Files.Dtos;

public sealed class PresignedUrlResponse
{
    public string Url { get; set; }
    public DateTime ExpiresAt { get; set; }
}
