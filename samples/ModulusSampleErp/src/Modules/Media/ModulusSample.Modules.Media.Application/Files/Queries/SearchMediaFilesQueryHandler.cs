namespace ModulusSample.Modules.Media.Application.Files.Queries;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;
using ModulusSample.Modules.Media.Application.Files.Queries;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;
using ModulusSample.Shared.Domain;

/// <summary>
/// Full-text style search across file names, alt text and descriptions.
/// </summary>
public sealed class SearchMediaFilesQueryHandler
    : IQueryHandler<SearchMediaFilesQuery, PagedResult<MediaFileDto>>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMediaStorageService _storageService;
    private readonly ILogger<SearchMediaFilesQueryHandler> _logger;

    public SearchMediaFilesQueryHandler(
        IMediaFileRepository mediaFileRepository,
        IMediaStorageService storageService,
        ILogger<SearchMediaFilesQueryHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<PagedResult<MediaFileDto>> HandleAsync(
        SearchMediaFilesQuery query,
        CancellationToken ct)
    {
        try
        {
            var files = await _mediaFileRepository.SearchAsync(query.SearchTerm, ct);
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
            _logger.LogError(ex, "Failed to search media files: {SearchTerm}", query.SearchTerm);
            throw;
        }
    }
}
