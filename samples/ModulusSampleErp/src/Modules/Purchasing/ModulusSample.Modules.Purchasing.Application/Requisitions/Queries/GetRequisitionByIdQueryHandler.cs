using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;

public sealed class GetRequisitionByIdQueryHandler(
    IRequisitionRepository repository) : IQueryHandler<GetRequisitionByIdQuery, PurchaseRequisitionDto?>
{
    public async Task<PurchaseRequisitionDto?> HandleAsync(
        GetRequisitionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var requisition = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (requisition is null)
            return null;

        return new PurchaseRequisitionDto(
            requisition.Id,
            requisition.RequisitionNumber,
            requisition.RequesterId,
            requisition.ApproverId,
            requisition.TotalAmount,
            requisition.Status,
            requisition.OrgUnitId,
            requisition.TenantId);
    }
}