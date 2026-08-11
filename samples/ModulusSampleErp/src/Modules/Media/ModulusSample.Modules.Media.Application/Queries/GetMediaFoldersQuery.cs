namespace ModulusSample.Modules.Media.Application.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;

public sealed record GetMediaFoldersQuery(Guid? ParentFolderId = null) : IQuery<IReadOnlyList<MediaFolderDto>>;
