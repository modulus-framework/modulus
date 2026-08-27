namespace ModulusSample.Modules.Media.Application.Folders.Commands;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Folders.Commands;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Renames a media folder and tracks the modification timestamp.
/// </summary>
public sealed class UpdateMediaFolderCommandHandler
    : ICommandHandler<UpdateMediaFolderCommand, Unit>
{
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateMediaFolderCommandHandler> _logger;

    public UpdateMediaFolderCommandHandler(
        IMediaFolderRepository mediaFolderRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateMediaFolderCommandHandler> logger)
    {
        _mediaFolderRepository = mediaFolderRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(
        UpdateMediaFolderCommand command,
        CancellationToken ct)
    {
        try
        {
            var folder = await _mediaFolderRepository.GetByIdAsync(command.FolderId, ct);
            if (folder is null)
            {
                throw new InvalidOperationException($"Media folder with ID {command.FolderId} not found.");
            }

            folder.UpdateInfo(command.Name, command.Description);

            await _mediaFolderRepository.UpdateAsync(folder, ct);
            await _unitOfWork.CommitAsync(ct);
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update media folder: {FolderId}", command.FolderId);
            throw;
        }
    }
}
