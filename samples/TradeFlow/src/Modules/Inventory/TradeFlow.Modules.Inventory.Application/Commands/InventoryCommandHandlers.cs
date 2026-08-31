using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Inventory.Application.Commands;
using TradeFlow.Modules.Inventory.Application.Dtos;
using TradeFlow.Modules.Inventory.Domain.Entities;
using TradeFlow.Modules.Inventory.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Inventory.Application.Commands;

public sealed class CreateStockItemHandler(
    IStockItemRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateStockItemCommand, Result<StockItemResponse>>
{
    public async Task<Result<StockItemResponse>> HandleAsync(CreateStockItemCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        StockItem item = StockItem.Create(tenantId, request.SiteId, request.ItemId, request.Sku, request.Name, request.Uom);

        await repository.AddAsync(item, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToStockItemResponse(item));
    }
}

public sealed class ReceiveGoodsHandler(
    IGrnRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<ReceiveGoodsCommand, Result<GrnResponse>>
{
    public async Task<Result<GrnResponse>> HandleAsync(ReceiveGoodsCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Grn grn;
        try
        {
            grn = Grn.Create(tenantId, request.FileId, request.PoId, request.VendorId, request.GrnNumber,
                request.ReceivedOn, currentUser.UserName ?? "system");

            foreach (ReceiveGoodsLineInput line in request.Lines)
            {
                grn.AddLine(line.ItemId, line.OrderedQty, line.ReceivedQty, line.OverReceiptTolerancePct,
                    line.ProvisionalUnitCost, line.SourceDocNumber);
            }
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<GrnResponse>(Error.Validation("Grn.Line", ex.Message));
        }

        await repository.AddAsync(grn, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToGrnResponse(grn));
    }
}

public sealed class PostGrnHandler(
    IGrnRepository repository,
    IStockItemRepository stockRepository,
    IInventoryValueLedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<PostGrnCommand, Result<GrnResponse>>
{
    public async Task<Result<GrnResponse>> HandleAsync(PostGrnCommand request, CancellationToken ct)
    {
        Grn? grn = await repository.GetByIdAsync(request.GrnId, ct);
        if (grn is null)
            return Result.Failure<GrnResponse>(Error.NotFound("Grn.NotFound", "GRN not found"));

        foreach (GrnLine line in grn.Lines)
        {
            StockItem? stock = await stockRepository.GetByItemAsync(grn.TenantId, grn.FileId, line.ItemId, ct);
            if (stock is null)
                continue;

            stock.Receive(line.ReceivedQty, line.ProvisionalUnitCost);
            await stockRepository.SaveAsync(stock, ct);

            var entry = InventoryValueLedgerEntry.Record(grn.TenantId, grn.FileId, line.ItemId,
                StockMovementType.GrnReceipt, line.ReceivedQty, line.ProvisionalUnitCost,
                decimal.Round(line.ReceivedQty * line.ProvisionalUnitCost, 4, MidpointRounding.ToEven),
                line.SourceDocNumber, grn.GrnNumber);
            await ledgerRepository.AddAsync(entry, ct);
        }

        grn.HoldForQc();
        await repository.SaveAsync(grn, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToGrnResponse(grn));
    }
}

public sealed class CreateQcInspectionHandler(
    IQcInspectionRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateQcInspectionCommand, Result<QcInspectionResponse>>
{
    public async Task<Result<QcInspectionResponse>> HandleAsync(CreateQcInspectionCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        QcInspection inspection;
        try
        {
            inspection = QcInspection.Create(tenantId, request.GrnId, request.InspectedOn, request.InspectedBy);
            foreach (QcInspectionLineInput line in request.Lines)
            {
                inspection.AddLine(line.GrnLineId, line.ItemId, line.InspectedQty, line.AcceptedQty,
                    line.Decision, line.Note);
            }
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<QcInspectionResponse>(Error.Validation("Qc.Line", ex.Message));
        }

        await repository.AddAsync(inspection, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToQcInspectionResponse(inspection));
    }
}

public sealed class CreateBatchHandler(
    IBatchRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateBatchCommand, Result<BatchResponse>>
{
    public async Task<Result<BatchResponse>> HandleAsync(CreateBatchCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Batch batch;
        try
        {
            batch = Batch.Create(tenantId, request.SiteId, request.ItemId, request.BatchNo, request.SourceDoc,
                request.Quantity, request.ExpiryDate, request.UnitCost);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<BatchResponse>(Error.Validation("Batch", ex.Message));
        }

        await repository.AddAsync(batch, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToBatchResponse(batch));
    }
}

public sealed class RevalueStockHandler(
    IStockItemRepository repository,
    IInventoryValueLedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<RevalueStockCommand, Result<StockItemResponse>>
{
    public async Task<Result<StockItemResponse>> HandleAsync(RevalueStockCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        StockItem? item = await repository.GetByItemAsync(tenantId, request.SiteId, request.ItemId, ct);
        if (item is null)
            return Result.Failure<StockItemResponse>(Error.NotFound("Stock.NotFound", "Stock item not found"));

        decimal delta = item.Revalue(request.NewUnitCost, request.Reference);
        await repository.SaveAsync(item, ct);

        var entry = InventoryValueLedgerEntry.Record(tenantId, request.SiteId, request.ItemId,
            StockMovementType.Revaluation, item.QuantityOnHand, request.NewUnitCost, delta,
            "LandedCostFinalized", request.Reference);
        await ledgerRepository.AddAsync(entry, ct);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToStockItemResponse(item));
    }
}

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed class CreateReturnDraftHandler(
    IGrnReturnDraftRepository repository,
    IGrnRepository grnRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateReturnDraftCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateReturnDraftCommand request, CancellationToken ct)
    {
        Grn? grn = await grnRepository.GetByIdAsync(request.GrnId, ct);
        if (grn is null)
            return Result.Failure<Guid>(Error.NotFound("Grn.NotFound", "GRN not found"));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var draft = GrnReturnDraft.Create(
            tenantId, request.GrnId, grn.PoId, grn.VendorId ?? Guid.Empty,
            grn.GrnNumber, request.CreatedOn, currentUser.UserName ?? "system");

        foreach (ReturnDraftLineInput line in request.Lines)
        {
            draft.AddLine(line.GrnLineId, line.ItemId, line.RejectedQty, line.UnitCost, line.Reason);
        }

        await repository.AddAsync(draft, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(draft.Id);
    }
}

public sealed class SubmitReturnDraftHandler(
    IGrnReturnDraftRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitReturnDraftCommand, Result>
{
    public async Task<Result> HandleAsync(SubmitReturnDraftCommand request, CancellationToken ct)
    {
        GrnReturnDraft? draft = await repository.GetByIdAsync(request.DraftId, ct);
        if (draft is null)
            return Result.Failure(Error.NotFound("ReturnDraft.NotFound", "Return draft not found"));

        Result result = draft.Submit(request.DebitNoteNumber);
        if (!result.IsSuccess)
            return result;

        await repository.SaveAsync(draft, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

// ── Stock Issue (FEFO batch consumption, BR-VAL-05) ───────────────

public sealed class IssueStockHandler(
    IStockItemRepository stockRepository,
    IBatchRepository batchRepository,
    IInventoryValueLedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<IssueStockCommand, Result<StockItemResponse>>
{
    public async Task<Result<StockItemResponse>> HandleAsync(IssueStockCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        StockItem? item = await stockRepository.GetByItemAsync(tenantId, request.SiteId, request.ItemId, ct);
        if (item is null)
            return Result.Failure<StockItemResponse>(Error.NotFound("Stock.NotFound", "Stock item not found"));

        Result issueResult = item.Issue(request.Quantity);
        if (!issueResult.IsSuccess)
            return Result.Failure<StockItemResponse>(issueResult.Error);

        // Consume from batches in FEFO order (soonest-expiring first)
        IReadOnlyList<Batch> batches = await batchRepository.GetForItemAsync(tenantId, request.SiteId, request.ItemId, ct);
        decimal remaining = request.Quantity;
        foreach (Batch batch in batches.Where(b => b.Quantity > 0m))
        {
            if (remaining <= 0m) break;
            decimal consume = Math.Min(remaining, batch.Quantity);
            batch.Consume(consume);
            remaining -= consume;
        }

        await stockRepository.SaveAsync(item, ct);

        var entry = InventoryValueLedgerEntry.Record(tenantId, request.SiteId, request.ItemId,
            StockMovementType.Issue, request.Quantity, item.WeightedAverageCost,
            decimal.Round(-request.Quantity * item.WeightedAverageCost, 4, MidpointRounding.ToEven),
            request.SourceDoc, request.Reference);
        await ledgerRepository.AddAsync(entry, ct);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToStockItemResponse(item));
    }
}

// ── Manual Stock Adjustment (BR-VAL-03, doc 08 §8.3) ─────────────

public sealed class CreateManualAdjustmentHandler(
    IStockItemRepository stockRepository,
    IInventoryValueLedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateManualAdjustmentCommand, Result<StockItemResponse>>
{
    public async Task<Result<StockItemResponse>> HandleAsync(CreateManualAdjustmentCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        StockItem? item = await stockRepository.GetByItemAsync(tenantId, request.SiteId, request.ItemId, ct);
        if (item is null)
            return Result.Failure<StockItemResponse>(Error.NotFound("Stock.NotFound", "Stock item not found"));

        if (request.QuantityDelta > 0m)
        {
            item.Receive(request.QuantityDelta, request.UnitCost);
        }
        else if (request.QuantityDelta < 0m)
        {
            Result issueResult = item.Issue(Math.Abs(request.QuantityDelta));
            if (!issueResult.IsSuccess)
                return Result.Failure<StockItemResponse>(issueResult.Error);
        }

        decimal valueDelta = decimal.Round(request.QuantityDelta * request.UnitCost, 4, MidpointRounding.ToEven);

        await stockRepository.SaveAsync(item, ct);

        var entry = InventoryValueLedgerEntry.Record(tenantId, request.SiteId, request.ItemId,
            StockMovementType.ManualAdjustment, request.QuantityDelta, request.UnitCost, valueDelta,
            request.SourceDoc, request.Reference);
        await ledgerRepository.AddAsync(entry, ct);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(InventoryResponseFactory.ToStockItemResponse(item));
    }
}