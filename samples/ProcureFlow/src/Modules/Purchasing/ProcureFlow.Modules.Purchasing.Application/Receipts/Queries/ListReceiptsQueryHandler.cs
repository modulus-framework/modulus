using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Queries;

public sealed class ListReceiptsQueryHandler(
    IGoodsReceiptRepository repository) : IQueryHandler<ListReceiptsQuery, PagedResult<GoodsReceiptDto>>
{
    public async Task<PagedResult<GoodsReceiptDto>> HandleAsync(
        ListReceiptsQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = page.Items.Select(r => new GoodsReceiptDto(
            r.Id,
            r.ReceiptNumber,
            r.PurchaseOrderId,
            r.ReceivedDate,
            r.Status,
            r.OrgUnitId,
            r.TenantId)).ToList();

        return new PagedResult<GoodsReceiptDto>(items, page.TotalCount, request.Page, request.PageSize);
    }
}