using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Application.Queries;

public sealed record ListSalesOrdersQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedResult<SalesOrderDto>>;
