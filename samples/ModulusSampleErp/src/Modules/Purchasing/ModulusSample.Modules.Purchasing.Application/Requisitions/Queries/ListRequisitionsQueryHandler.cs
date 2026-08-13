using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;

public sealed class ListRequisitionsQueryHandler(
    IRequisitionRepository repository) : IQueryHandler<ListRequisitionsQuery, PagedResult<PurchaseRequisitionDto>>
{
    public async Task<PagedResult<PurchaseRequisitionDto>> HandleAsync(
        ListRequisitionsQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = page.Items.Select(r => new PurchaseRequisitionDto(
            r.Id,
            r.RequisitionNumber,
            r.RequesterId,
            r.ApproverId,
            r.TotalAmount,
            r.Status,
            r.OrgUnitId,
            r.TenantId)).ToList();

        return new PagedResult<PurchaseRequisitionDto>(items, page.TotalCount, request.Page, request.PageSize);
    }
}