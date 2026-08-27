namespace ModulusSample.Modules.Media.Application.Folders.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Folders.Dtos;

public sealed record GetMediaFolderByIdQuery(Guid FolderId) : IQuery<MediaFolderDto?>;
