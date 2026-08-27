namespace ModulusSample.Modules.Media.Application.Folders.Commands;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Folders.Commands;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Renames a media folder. Deleting a non-empty folder is rejected so files
/// and subfolders are never orphaned.
/// </summary>
public sealed class DeleteMediaFolderCommandHandler
    : ICommandHandler<DeleteMediaFolderCommand, Unit>
{
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteMediaFolderCommandHandler> _logger;

    public DeleteMediaFolderCommandHandler(
        IMediaFolderRepository mediaFolderRepository,
        IMediaFileRepository mediaFileRepository,
        IUnitOfWork unitOfWork,
        ILogger<DeleteMediaFolderCommandHandler> logger)
    {
        _mediaFolderRepository = mediaFolderRepository;
        _mediaFileRepository = mediaFileRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(
        DeleteMediaFolderCommand command,
        CancellationToken ct)
    {
        try
        {
            var folder = await _mediaFolderRepository.GetByIdAsync(command.FolderId, ct);
            if (folder is null)
            {
                throw new InvalidOperationException($"Media folder with ID {command.FolderId} not found.");
            }

            var childFolders = await _mediaFolderRepository.GetByParentFolderIdAsync(command.FolderId, ct);
            if (childFolders.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot delete folder '{folder.Name}' because it contains {childFolders.Count} subfolder(s). " +
                    "Please delete or move the subfolders first.");
            }

            var filesInFolder = await _mediaFileRepository.GetByFolderIdAsync(command.FolderId, ct);
            if (filesInFolder.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot delete folder '{folder.Name}' because it contains {filesInFolder.Count} file(s). " +
                    "Please delete or move the files first.");
            }

            await _mediaFolderRepository.DeleteAsync(folder, ct);
            await _unitOfWork.CommitAsync(ct);
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete media folder: {FolderId}", command.FolderId);
            throw;
        }
    }
}
