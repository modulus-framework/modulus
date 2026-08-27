namespace ModulusSample.Modules.Media.Application.Files.Queries;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;
using ModulusSample.Modules.Media.Application.Files.Queries;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;

/// <summary>
/// Returns a single media file with its (temporarily) presigned URLs.
/// </summary>
public sealed class GetMediaFileByIdQueryHandler
    : IQueryHandler<GetMediaFileByIdQuery, MediaFileDto?>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMediaStorageService _storageService;
    private readonly ILogger<GetMediaFileByIdQueryHandler> _logger;

    public GetMediaFileByIdQueryHandler(
        IMediaFileRepository mediaFileRepository,
        IMediaStorageService storageService,
        ILogger<GetMediaFileByIdQueryHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<MediaFileDto?> HandleAsync(
        GetMediaFileByIdQuery query,
        CancellationToken ct)
    {
        try
        {
            var mediaFile = await _mediaFileRepository.GetByIdAsync(query.MediaFileId, ct);
            if (mediaFile is null)
            {
                return null;
            }

            var fileUrl = await _storageService.GetPresignedUrlAsync(mediaFile.StoragePath, TimeSpan.FromHours(1), ct);
            string? thumbnailUrl = null;

            if (!string.IsNullOrWhiteSpace(mediaFile.ThumbnailPath))
            {
                thumbnailUrl = await _storageService.GetPresignedUrlAsync(mediaFile.ThumbnailPath, TimeSpan.FromHours(1), ct);
            }

            return MediaFileDtoMapper.ToDto(mediaFile, fileUrl, thumbnailUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media file: {MediaFileId}", query.MediaFileId);
            throw;
        }
    }
}
