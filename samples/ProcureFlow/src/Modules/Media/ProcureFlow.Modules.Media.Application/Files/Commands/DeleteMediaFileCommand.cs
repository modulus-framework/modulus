namespace ModulusSample.Modules.Media.Application.Files.Commands;

using Modulus.Mediator.Abstractions;

public sealed record DeleteMediaFileCommand(Guid MediaFileId) : ICommand;
