using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Application.Queries;

public sealed record ListProductsQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<ProductDto>>;
