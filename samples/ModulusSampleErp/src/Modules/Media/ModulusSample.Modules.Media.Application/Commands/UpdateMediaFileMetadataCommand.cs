namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;

public sealed record UpdateMediaFileMetadataCommand(
    Guid MediaFileId,
    string? AltText = null,
    string? Description = null,
    Guid? FolderId = null) : ICommand;
