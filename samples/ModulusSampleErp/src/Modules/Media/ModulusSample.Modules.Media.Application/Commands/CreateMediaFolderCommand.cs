namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;

public sealed record CreateMediaFolderCommand(
    string Name,
    string? Description = null,
    Guid? ParentFolderId = null) : ICommand<MediaFolderDto>;
