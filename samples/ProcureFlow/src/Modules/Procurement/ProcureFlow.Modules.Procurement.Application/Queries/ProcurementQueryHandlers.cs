using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Procurement.Application.Dtos;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Modules.Procurement.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Application.Queries;

public sealed class GetPrHandler(
    IPrRepository repository) : IQueryHandler<GetPrQuery, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(GetPrQuery request, CancellationToken ct)
    {
        PurchaseRequisition? pr = await repository.GetByIdAsync(request.PrId, ct);
        return pr is null
            ? Result.Failure<PurchaseRequisitionResponse>(Error.NotFound("Pr.NotFound", "PR not found"))
            : Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class GetRfqHandler(
    IRfqRepository repository) : IQueryHandler<GetRfqQuery, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(GetRfqQuery request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        return rfq is null
            ? Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"))
            : Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class GetPoHandler(
    IPoRepository repository) : IQueryHandler<GetPoQuery, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(GetPoQuery request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        return po is null
            ? Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"))
            : Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class ListPrsHandler(
    IPrRepository repository,
    Modulus.Core.Abstractions.ICurrentTenant currentTenant) : IQueryHandler<ListPrsQuery, Result<IReadOnlyList<PurchaseRequisitionResponse>>>
{
    public async Task<Result<IReadOnlyList<PurchaseRequisitionResponse>>> HandleAsync(ListPrsQuery request, CancellationToken ct)
    {
        IReadOnlyList<PurchaseRequisition> items = await repository.GetAllAsync(currentTenant.TenantId ?? Guid.Empty, ct);
        return Result.Success<IReadOnlyList<PurchaseRequisitionResponse>>(
            items.OrderByDescending(p => p.CreatedOn).Select(ProcurementResponseFactory.ToPrResponse).ToList());
    }
}

public sealed class ListPosHandler(
    IPoRepository repository,
    Modulus.Core.Abstractions.ICurrentTenant currentTenant) : IQueryHandler<ListPosQuery, Result<IReadOnlyList<PurchaseOrderResponse>>>
{
    public async Task<Result<IReadOnlyList<PurchaseOrderResponse>>> HandleAsync(ListPosQuery request, CancellationToken ct)
    {
        IReadOnlyList<PurchaseOrder> items = await repository.GetAllAsync(currentTenant.TenantId ?? Guid.Empty, ct);
        return Result.Success<IReadOnlyList<PurchaseOrderResponse>>(
            items.OrderByDescending(p => p.CreatedBy).Select(ProcurementResponseFactory.ToPoResponse).ToList());
    }
}