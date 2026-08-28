using ProcureFlow.Modules.Inventory.Application.Dtos;
using ProcureFlow.Modules.Inventory.Domain.Entities;

namespace ProcureFlow.Modules.Inventory.Application;

public static class InventoryResponseFactory
{
    public static GrnResponse ToGrnResponse(Grn grn) => new(
        grn.Id, grn.TenantId, grn.FileId, grn.PoId, grn.VendorId, grn.GrnNumber, grn.ReceivedOn, grn.Status,
        grn.Lines.Select(l => new GrnLineResponse(l.Id, l.ItemId, l.OrderedQty, l.ReceivedQty,
            l.ProvisionalUnitCost, l.SourceDocNumber)).ToArray());

    public static StockItemResponse ToStockItemResponse(StockItem item) => new(
        item.Id, item.TenantId, item.SiteId, item.ItemId, item.Sku, item.Name, item.Uom,
        item.QuantityOnHand, item.WeightedAverageCost, item.InventoryValue);

    public static QcInspectionResponse ToQcInspectionResponse(QcInspection inspection) => new(
        inspection.Id, inspection.GrnId, inspection.InspectedOn, inspection.InspectedBy,
        inspection.Lines.Select(l => new QcInspectionLineResponse(l.Id, l.GrnLineId, l.ItemId, l.InspectedQty,
            l.AcceptedQty, l.Decision, l.Note)).ToArray());

    public static BatchResponse ToBatchResponse(Batch batch) => new(
        batch.Id, batch.SiteId, batch.ItemId, batch.BatchNo, batch.SourceDoc, batch.Quantity,
        batch.ExpiryDate, batch.UnitCost);

    public static InventoryValueLedgerEntryResponse ToLedgerResponse(InventoryValueLedgerEntry entry) => new(
        entry.Id, entry.SiteId, entry.ItemId, entry.TxnType, entry.Quantity, entry.UnitCost, entry.ValueDelta,
        entry.SourceDoc, entry.Reference, entry.OccurredAtUtc);

    // ── GRN Return Draft (BR-GRN-02) ────────────────────────────────

    public static GrnReturnDraftResponse ToReturnDraftResponse(GrnReturnDraft draft) => new(
        draft.Id, draft.GrnId, draft.VendorId, draft.GrnNumber, draft.CreatedOn,
        draft.Status, draft.TotalCreditAmount, draft.DebitNoteNumber, draft.CreatedBy,
        draft.Lines.Select(l => new GrnReturnDraftLineResponse(
            l.Id, l.GrnLineId, l.ItemId, l.RejectedQty, l.UnitCost, l.Reason, l.LineTotal)).ToArray());
}