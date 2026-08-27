namespace ModulusSample.Modules.Media.Application.Files.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;
using ModulusSample.Shared.Domain;

public sealed record SearchMediaFilesQuery(string SearchTerm, int Page = 1, int PageSize = 20) : IQuery<PagedResult<MediaFileDto>>;
