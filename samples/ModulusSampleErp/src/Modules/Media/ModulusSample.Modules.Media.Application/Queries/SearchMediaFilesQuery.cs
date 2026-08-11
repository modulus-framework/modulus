namespace ModulusSample.Modules.Media.Application.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Shared.Domain;

public sealed record SearchMediaFilesQuery(string SearchTerm, int Page = 1, int PageSize = 20) : IQuery<PagedResult<MediaFileDto>>;
