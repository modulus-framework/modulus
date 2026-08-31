using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Procurement.Application.Commands;
using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Modules.Procurement.Application.Queries;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Modules.Procurement.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Application.Queries;

public sealed class GetContractByIdQueryHandler(
    IContractRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetContractByIdQuery, Result<ContractDetailResponse>>
{
    public async Task<Result<ContractDetailResponse>> HandleAsync(GetContractByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractDetailResponse>(ContractErrors.NotFound);

        return Result.Success(ToDetailResponse(contract));
    }

    private static ContractDetailResponse ToDetailResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc,
        c.Lines.Select(l => new ContractLineResponse(l.Id, l.ItemId, l.FreeText, l.UnitPrice, l.MinQuantity, l.EscalationJson, l.Notes)).ToList(),
        c.Documents.Select(d => new ContractDocumentResponse(d.Id, d.DocumentType, d.S3Key, d.ExpiryDate, d.UploadedBy, d.UploadedAtUtc)).ToList(),
        c.Milestones.Select(m => new ContractMilestoneResponse(m.Id, m.Title, m.DueDate, m.Deliverables, m.SlaJson, m.IsCompleted, m.CompletedAtUtc)).ToList(),
        c.Revisions.Select(r => new ContractRevisionResponse(r.Version, r.Reason, r.By, r.AtUtc, r.PreviousEndDate, r.NewEndDate, r.PreviousCapValue, r.NewCapValue)).ToList());
}

public sealed class ListContractsQueryHandler(
    IContractRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListContractsQuery, Result<IReadOnlyList<ContractResponse>>>
{
    public async Task<Result<IReadOnlyList<ContractResponse>>> HandleAsync(ListContractsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contracts = await repository.GetAllAsync(tenantId, request.Status, request.VendorId, ct);
        return Result.Success<IReadOnlyList<ContractResponse>>(contracts.Select(ToResponse).ToList());
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class GetExpiringContractsQueryHandler(
    IContractRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetExpiringContractsQuery, Result<IReadOnlyList<ContractResponse>>>
{
    public async Task<Result<IReadOnlyList<ContractResponse>>> HandleAsync(GetExpiringContractsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contracts = await repository.GetExpiringAsync(tenantId, request.WithinDays, ct);
        return Result.Success<IReadOnlyList<ContractResponse>>(contracts.Select(ToResponse).ToList());
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class CheckMaverickPurchaseQueryHandler(
    IContractRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<CheckMaverickPurchaseQuery, Result<MaverickCheckResponse>>
{
    public async Task<Result<MaverickCheckResponse>> HandleAsync(CheckMaverickPurchaseQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetActiveByVendorAndItemAsync(request.VendorId, request.ItemId, tenantId, ct);

        if (contract is null)
            return Result.Success(new MaverickCheckResponse(false, null, null, null));

        var line = contract.Lines.FirstOrDefault(l => l.ItemId == request.ItemId);
        return Result.Success(new MaverickCheckResponse(
            false, contract.Id, contract.ContractNumber, line?.UnitPrice));
    }
}
