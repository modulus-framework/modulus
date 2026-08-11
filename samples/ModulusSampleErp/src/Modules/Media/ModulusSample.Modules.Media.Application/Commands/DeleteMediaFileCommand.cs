namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;

public sealed record DeleteMediaFileCommand(Guid MediaFileId) : ICommand;
