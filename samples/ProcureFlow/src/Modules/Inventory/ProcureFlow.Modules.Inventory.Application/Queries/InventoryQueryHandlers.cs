using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Inventory.Application.Dtos;
using ProcureFlow.Modules.Inventory.Application.Queries;
using ProcureFlow.Modules.Inventory.Domain.Entities;
using ProcureFlow.Modules.Inventory.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Inventory.Application.Queries;

public sealed class GetStockItemHandler(
    IStockItemRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetStockItemQuery, Result<StockItemResponse>>
{
    public async Task<Result<StockItemResponse>> HandleAsync(GetStockItemQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        StockItem? item = await repository.GetByItemAsync(tenantId, query.SiteId, query.ItemId, ct);
        return item is null
            ? Result.Failure<StockItemResponse>(Error.NotFound("Stock.NotFound", "Stock item not found"))
            : Result.Success(InventoryResponseFactory.ToStockItemResponse(item));
    }
}

public sealed class GetGrnHandler(IGrnRepository repository) : IQueryHandler<GetGrnQuery, Result<GrnResponse>>
{
    public async Task<Result<GrnResponse>> HandleAsync(GetGrnQuery query, CancellationToken ct)
    {
        Grn? grn = await repository.GetByIdAsync(query.GrnId, ct);
        return grn is null
            ? Result.Failure<GrnResponse>(Error.NotFound("Grn.NotFound", "GRN not found"))
            : Result.Success(InventoryResponseFactory.ToGrnResponse(grn));
    }
}

public sealed class GetQcInspectionHandler(IQcInspectionRepository repository) : IQueryHandler<GetQcInspectionQuery, Result<QcInspectionResponse>>
{
    public async Task<Result<QcInspectionResponse>> HandleAsync(GetQcInspectionQuery query, CancellationToken ct)
    {
        QcInspection? inspection = await repository.GetByGrnAsync(query.GrnId, ct);
        return inspection is null
            ? Result.Failure<QcInspectionResponse>(Error.NotFound("Qc.NotFound", "No QC inspection for GRN"))
            : Result.Success(InventoryResponseFactory.ToQcInspectionResponse(inspection));
    }
}

public sealed class ListBatchesHandler(
    IBatchRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListBatchesQuery, Result<IReadOnlyList<BatchResponse>>>
{
    public async Task<Result<IReadOnlyList<BatchResponse>>> HandleAsync(ListBatchesQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<Batch> batches = await repository.GetForItemAsync(tenantId, query.SiteId, query.ItemId, ct);
        return Result.Success<IReadOnlyList<BatchResponse>>(batches.Select(InventoryResponseFactory.ToBatchResponse).ToArray());
    }
}

public sealed class ListLedgerEntriesHandler(
    IInventoryValueLedgerRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListLedgerEntriesQuery, Result<IReadOnlyList<InventoryValueLedgerEntryResponse>>>
{
    public async Task<Result<IReadOnlyList<InventoryValueLedgerEntryResponse>>> HandleAsync(ListLedgerEntriesQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<InventoryValueLedgerEntry> entries = await repository.GetForItemAsync(tenantId, query.SiteId, query.ItemId, ct);
        return Result.Success<IReadOnlyList<InventoryValueLedgerEntryResponse>>(entries.Select(InventoryResponseFactory.ToLedgerResponse).ToArray());
    }
}

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed class GetReturnDraftHandler(
    IGrnReturnDraftRepository repository) : IQueryHandler<GetReturnDraftQuery, Result<GrnReturnDraftResponse>>
{
    public async Task<Result<GrnReturnDraftResponse>> HandleAsync(GetReturnDraftQuery query, CancellationToken ct)
    {
        GrnReturnDraft? draft = await repository.GetByIdAsync(query.DraftId, ct);
        return draft is null
            ? Result.Failure<GrnReturnDraftResponse>(Error.NotFound("ReturnDraft.NotFound", "Return draft not found"))
            : Result.Success(InventoryResponseFactory.ToReturnDraftResponse(draft));
    }
}

public sealed class ListReturnDraftsByGrnHandler(
    IGrnReturnDraftRepository repository) : IQueryHandler<ListReturnDraftsByGrnQuery, Result<IReadOnlyList<GrnReturnDraftResponse>>>
{
    public async Task<Result<IReadOnlyList<GrnReturnDraftResponse>>> HandleAsync(ListReturnDraftsByGrnQuery query, CancellationToken ct)
    {
        IReadOnlyList<GrnReturnDraft> drafts = await repository.GetByGrnIdAsync(query.GrnId, ct);
        return Result.Success<IReadOnlyList<GrnReturnDraftResponse>>(drafts.Select(InventoryResponseFactory.ToReturnDraftResponse).ToArray());
    }
}