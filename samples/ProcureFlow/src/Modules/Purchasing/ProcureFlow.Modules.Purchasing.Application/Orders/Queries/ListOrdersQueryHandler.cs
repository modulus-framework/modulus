using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Queries;

public sealed class ListOrdersQueryHandler(
    IPurchaseOrderRepository repository) : IQueryHandler<ListOrdersQuery, PagedResult<PurchaseOrderDto>>
{
    public async Task<PagedResult<PurchaseOrderDto>> HandleAsync(
        ListOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = page.Items.Select(o => new PurchaseOrderDto(
            o.Id,
            o.OrderNumber,
            o.RequisitionId,
            o.SupplierId,
            o.TotalAmount,
            o.Status,
            o.OrgUnitId,
            o.TenantId)).ToList();

        return new PagedResult<PurchaseOrderDto>(items, page.TotalCount, request.Page, request.PageSize);
    }
}