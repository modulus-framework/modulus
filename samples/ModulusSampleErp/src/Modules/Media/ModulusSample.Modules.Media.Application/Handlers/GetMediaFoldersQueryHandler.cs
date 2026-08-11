namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Application.Queries;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Lists media folders, optionally scoped to a parent folder.
/// </summary>
public sealed class GetMediaFoldersQueryHandler
    : IQueryHandler<GetMediaFoldersQuery, IReadOnlyList<MediaFolderDto>>
{
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly ILogger<GetMediaFoldersQueryHandler> _logger;

    public GetMediaFoldersQueryHandler(
        IMediaFolderRepository mediaFolderRepository,
        ILogger<GetMediaFoldersQueryHandler> logger)
    {
        _mediaFolderRepository = mediaFolderRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MediaFolderDto>> HandleAsync(
        GetMediaFoldersQuery query,
        CancellationToken ct)
    {
        try
        {
            var folders = await _mediaFolderRepository.GetByParentFolderIdAsync(query.ParentFolderId, ct);
            var dtos = new List<MediaFolderDto>(folders.Count);

            foreach (var folder in folders)
            {
                var childFolders = await _mediaFolderRepository.GetByParentFolderIdAsync(folder.Id, ct);
                string? parentName = null;

                if (folder.ParentFolderId.HasValue)
                {
                    var parent = await _mediaFolderRepository.GetByIdAsync(folder.ParentFolderId.Value, ct);
                    parentName = parent?.Name;
                }

                dtos.Add(MediaFolderDtoMapper.ToDto(folder, childFolders.Count, parentName));
            }

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media folders for parent: {ParentFolderId}", query.ParentFolderId);
            throw;
        }
    }
}
