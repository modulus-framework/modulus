namespace ModulusSample.Modules.Media.Application.Handlers;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Repositories;

/// <summary>
/// Creates a media folder and generates a unique storage path for it.
/// </summary>
public sealed class CreateMediaFolderCommandHandler
    : ICommandHandler<CreateMediaFolderCommand, MediaFolderDto>
{
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<CreateMediaFolderCommandHandler> _logger;

    public CreateMediaFolderCommandHandler(
        IMediaFolderRepository mediaFolderRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ILogger<CreateMediaFolderCommandHandler> logger)
    {
        _mediaFolderRepository = mediaFolderRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<MediaFolderDto> HandleAsync(
        CreateMediaFolderCommand command,
        CancellationToken ct)
    {
        try
        {
            var path = await _mediaFolderRepository.GenerateUniquePathAsync(command.Name, command.ParentFolderId, ct);

            var folder = new MediaFolder(
                Guid.NewGuid(),
                command.Name,
                command.Description,
                command.ParentFolderId,
                path,
                _currentTenant.TenantId,
                _currentUser.UserId);

            await _mediaFolderRepository.AddAsync(folder, ct);
            await _unitOfWork.CommitAsync(ct);

            return new MediaFolderDto
            {
                Id = folder.Id,
                Name = folder.Name,
                Description = folder.Description,
                ParentFolderId = folder.ParentFolderId,
                Path = folder.Path,
                FileCount = folder.FileCount,
                ChildFolderCount = 0,
                TenantId = folder.TenantId,
                CreatedBy = folder.CreatedBy,
                CreatedAt = folder.CreatedAt,
                UpdatedAt = folder.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create media folder: {Name}", command.Name);
            throw;
        }
    }
}
