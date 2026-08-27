namespace ModulusSample.Modules.Media.Application.Folders.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Folders.Dtos;

public sealed record GetMediaFoldersQuery(Guid? ParentFolderId = null) : IQuery<IReadOnlyList<MediaFolderDto>>;
