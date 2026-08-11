namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;

public sealed record DeleteMediaFolderCommand(Guid FolderId) : ICommand;
