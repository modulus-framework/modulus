using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Application.Orders.Queries;

public sealed record ListSalesOrdersQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedResult<SalesOrderDto>>;