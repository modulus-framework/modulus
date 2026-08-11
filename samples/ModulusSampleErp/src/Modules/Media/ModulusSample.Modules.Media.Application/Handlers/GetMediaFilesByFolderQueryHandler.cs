namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Application.Queries;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;
using ModulusSample.Shared.Domain;

/// <summary>
/// Lists media files in a folder (or the root when no folder is given),
/// newest first.
/// </summary>
public sealed class GetMediaFilesByFolderQueryHandler
    : IQueryHandler<GetMediaFilesByFolderQuery, PagedResult<MediaFileDto>>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMediaStorageService _storageService;
    private readonly ILogger<GetMediaFilesByFolderQueryHandler> _logger;

    public GetMediaFilesByFolderQueryHandler(
        IMediaFileRepository mediaFileRepository,
        IMediaStorageService storageService,
        ILogger<GetMediaFilesByFolderQueryHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<PagedResult<MediaFileDto>> HandleAsync(
        GetMediaFilesByFolderQuery query,
        CancellationToken ct)
    {
        try
        {
            var files = query.FolderId.HasValue
                ? await _mediaFileRepository.GetByFolderIdAsync(query.FolderId.Value, ct)
                : await _mediaFileRepository.GetAllAsync(ct);

            var totalCount = files.Count;
            var pagedFiles = files
                .OrderByDescending(f => f.CreatedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            var dtos = new List<MediaFileDto>(pagedFiles.Count);
            foreach (var file in pagedFiles)
            {
                var fileUrl = await _storageService.GetPresignedUrlAsync(file.StoragePath, TimeSpan.FromHours(1), ct);
                string? thumbnailUrl = null;
                if (!string.IsNullOrWhiteSpace(file.ThumbnailPath))
                {
                    thumbnailUrl = await _storageService.GetPresignedUrlAsync(file.ThumbnailPath, TimeSpan.FromHours(1), ct);
                }
                dtos.Add(MediaFileDtoMapper.ToDto(file, fileUrl, thumbnailUrl));
            }

            return new PagedResult<MediaFileDto>(dtos, totalCount, query.Page, query.PageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media files for folder: {FolderId}", query.FolderId);
            throw;
        }
    }
}
