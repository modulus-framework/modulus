using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Application.Products.Queries;

public sealed record ListProductsQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<ProductDto>>;