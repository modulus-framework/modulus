namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Updates the searchable metadata of a media file (alt text / description).
/// </summary>
public sealed class UpdateMediaFileMetadataCommandHandler
    : ICommandHandler<UpdateMediaFileMetadataCommand, Unit>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateMediaFileMetadataCommandHandler> _logger;

    public UpdateMediaFileMetadataCommandHandler(
        IMediaFileRepository mediaFileRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateMediaFileMetadataCommandHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(
        UpdateMediaFileMetadataCommand command,
        CancellationToken ct)
    {
        try
        {
            var mediaFile = await _mediaFileRepository.GetByIdAsync(command.MediaFileId, ct);
            if (mediaFile is null)
            {
                throw new InvalidOperationException($"Media file with ID {command.MediaFileId} not found.");
            }

            mediaFile.UpdateMetadata(command.AltText, command.Description);

            await _mediaFileRepository.UpdateAsync(mediaFile, ct);
            await _unitOfWork.CommitAsync(ct);
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update media file metadata: {MediaFileId}", command.MediaFileId);
            throw;
        }
    }
}
