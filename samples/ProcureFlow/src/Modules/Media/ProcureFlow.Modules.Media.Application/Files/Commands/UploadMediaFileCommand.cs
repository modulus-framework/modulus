namespace ModulusSample.Modules.Media.Application.Files.Commands;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;

public sealed record UploadMediaFileCommand(
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileContent,
    Guid? FolderId = null,
    string? AltText = null,
    string? Description = null) : ICommand<UploadMediaFileResponse>;
