using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Queries;

public sealed class GetOrderByIdQueryHandler(
    IPurchaseOrderRepository repository) : IQueryHandler<GetOrderByIdQuery, PurchaseOrderDto?>
{
    public async Task<PurchaseOrderDto?> HandleAsync(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (order is null)
            return null;

        return new PurchaseOrderDto(
            order.Id,
            order.OrderNumber,
            order.RequisitionId,
            order.SupplierId,
            order.TotalAmount,
            order.Status,
            order.OrgUnitId,
            order.TenantId);
    }
}