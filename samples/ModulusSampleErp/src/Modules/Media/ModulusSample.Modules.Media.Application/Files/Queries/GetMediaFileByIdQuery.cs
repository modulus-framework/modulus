namespace ModulusSample.Modules.Media.Application.Files.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;

public sealed record GetMediaFileByIdQuery(Guid MediaFileId) : IQuery<MediaFileDto?>;
