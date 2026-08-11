namespace ModulusSample.Modules.Media.Application.Commands;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;

public sealed record UploadMediaFileCommand(
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileContent,
    Guid? FolderId = null,
    string? AltText = null,
    string? Description = null) : ICommand<UploadMediaFileResponse>;
