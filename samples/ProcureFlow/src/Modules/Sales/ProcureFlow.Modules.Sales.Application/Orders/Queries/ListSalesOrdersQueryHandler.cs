using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;
using ModulusSample.Modules.Sales.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Application.Orders.Queries;

public sealed class ListSalesOrdersQueryHandler(ISalesOrderRepository repository)
    : IQueryHandler<ListSalesOrdersQuery, PagedResult<SalesOrderDto>>
{
    public async Task<PagedResult<SalesOrderDto>> HandleAsync(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = orders.Items.Select(o => new SalesOrderDto(
            o.Id,
            o.OrderNumber,
            o.CustomerId,
            o.Status,
            o.TotalAmount,
            o.OrgUnitId,
            o.TenantId)).ToList();

        return new PagedResult<SalesOrderDto>(items, orders.TotalCount, request.Page, request.PageSize);
    }
}