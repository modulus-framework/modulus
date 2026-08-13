using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;
using ModulusSample.Modules.Sales.Domain.Repositories;

namespace ModulusSample.Modules.Sales.Application.Orders.Queries;

public sealed class GetSalesOrderByIdQueryHandler(ISalesOrderRepository repository)
    : IQueryHandler<GetSalesOrderByIdQuery, SalesOrderDto?>
{
    public async Task<SalesOrderDto?> HandleAsync(GetSalesOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (order is null)
            return null;

        return new SalesOrderDto(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Status,
            order.TotalAmount,
            order.OrgUnitId,
            order.TenantId);
    }
}