namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;

/// <summary>
/// Deletes a media file from both the object store and the database, keeping
/// the parent folder's file count in sync.
/// </summary>
public sealed class DeleteMediaFileCommandHandler
    : ICommandHandler<DeleteMediaFileCommand, Unit>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly IMediaStorageService _storageService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteMediaFileCommandHandler> _logger;

    public DeleteMediaFileCommandHandler(
        IMediaFileRepository mediaFileRepository,
        IMediaFolderRepository mediaFolderRepository,
        IMediaStorageService storageService,
        IUnitOfWork unitOfWork,
        ILogger<DeleteMediaFileCommandHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _mediaFolderRepository = mediaFolderRepository;
        _storageService = storageService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(
        DeleteMediaFileCommand command,
        CancellationToken ct)
    {
        try
        {
            var mediaFile = await _mediaFileRepository.GetByIdAsync(command.MediaFileId, ct);
            if (mediaFile is null)
            {
                throw new InvalidOperationException($"Media file with ID {command.MediaFileId} not found.");
            }

            await _storageService.DeleteFileAsync(mediaFile.StoragePath, ct);

            if (!string.IsNullOrWhiteSpace(mediaFile.ThumbnailPath))
            {
                await _storageService.DeleteFileAsync(mediaFile.ThumbnailPath, ct);
            }

            if (mediaFile.FolderId.HasValue)
            {
                var folder = await _mediaFolderRepository.GetByIdAsync(mediaFile.FolderId.Value, ct);
                if (folder is not null)
                {
                    folder.DecrementFileCount();
                    await _mediaFolderRepository.UpdateAsync(folder, ct);
                }
            }

            await _mediaFileRepository.DeleteAsync(mediaFile, ct);
            await _unitOfWork.CommitAsync(ct);
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete media file: {MediaFileId}", command.MediaFileId);
            throw;
        }
    }
}
