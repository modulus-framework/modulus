namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;

public sealed record UpdateMediaFolderCommand(
    Guid FolderId,
    string Name,
    string? Description = null) : ICommand;
