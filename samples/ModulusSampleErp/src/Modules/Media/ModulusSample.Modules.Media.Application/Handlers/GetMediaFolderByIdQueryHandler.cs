namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Application.Queries;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Returns a single media folder with its child-folder count.
/// </summary>
public sealed class GetMediaFolderByIdQueryHandler
    : IQueryHandler<GetMediaFolderByIdQuery, MediaFolderDto?>
{
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly ILogger<GetMediaFolderByIdQueryHandler> _logger;

    public GetMediaFolderByIdQueryHandler(
        IMediaFolderRepository mediaFolderRepository,
        ILogger<GetMediaFolderByIdQueryHandler> logger)
    {
        _mediaFolderRepository = mediaFolderRepository;
        _logger = logger;
    }

    public async Task<MediaFolderDto?> HandleAsync(
        GetMediaFolderByIdQuery query,
        CancellationToken ct)
    {
        try
        {
            var folder = await _mediaFolderRepository.GetByIdAsync(query.FolderId, ct);
            if (folder is null)
            {
                return null;
            }

            var childFolders = await _mediaFolderRepository.GetByParentFolderIdAsync(folder.Id, ct);
            string? parentName = null;
            if (folder.ParentFolderId.HasValue)
            {
                var parent = await _mediaFolderRepository.GetByIdAsync(folder.ParentFolderId.Value, ct);
                parentName = parent?.Name;
            }

            return MediaFolderDtoMapper.ToDto(folder, childFolders.Count, parentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media folder: {FolderId}", query.FolderId);
            throw;
        }
    }
}
