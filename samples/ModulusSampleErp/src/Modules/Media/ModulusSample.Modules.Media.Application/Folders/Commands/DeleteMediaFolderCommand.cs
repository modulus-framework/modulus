namespace ModulusSample.Modules.Media.Application.Folders.Commands;

using Modulus.Mediator.Abstractions;

public sealed record DeleteMediaFolderCommand(Guid FolderId) : ICommand;
