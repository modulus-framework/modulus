using TradeFlow.Modules.Procurement.Domain.Entities;

namespace TradeFlow.Modules.Procurement.Application.Dtos;

public static class ProcurementResponseFactory
{
    public static PurchaseRequisitionResponse ToPrResponse(PurchaseRequisition pr) => new(
        pr.Id, pr.TenantId, pr.PrNumber, pr.RequesterName, pr.Status, pr.CreatedOn, pr.EstimatedTotal,
        pr.RejectionReason, pr.CancellationReason,
        pr.Lines.Select(l => new PrLineResponse(l.Id, l.ItemId, l.FreeText, l.Category, l.Quantity, l.Uom,
            l.NeedByDate, l.SuggestedVendorId, l.EstimatedUnitPrice, l.EstimatedTotal, l.Currency,
            l.NeedByWarning)).ToList());

    public static RfqResponse ToRfqResponse(Rfq rfq) => new(
        rfq.Id, rfq.TenantId, rfq.RfqNumber, rfq.Title, rfq.IsSealed, rfq.DeadlineUtc, rfq.MinBidders,
        rfq.Currency, rfq.CreatedBy, rfq.Status,
        rfq.Lines.Select(l => new RfqLineResponse(l.Id, l.PrLineId, l.ItemId, l.FreeText, l.HsCode, l.Quantity,
            l.Uom, l.PortOfLoading, l.PortOfDischarge, l.IsImport)).ToList(),
        rfq.Invitations.Select(i => i.VendorId).ToList(),
        rfq.Bids.Select(b => new RfqBidResponse(b.Id, b.VendorId, b.BidNo, b.TotalAmountFcy, b.Currency,
            b.SubmittedAtUtc, b.IsLate)).ToList(),
        rfq.Comparison.Select(c => new RfqComparisonRowResponse(c.BidId, c.VendorId, c.BidAmountFcy, c.Currency,
            c.FreightBdt, c.DutyBdt, c.HandlingBdt, c.LandedTotalBdt)).ToList(),
        rfq.Award is null ? null : new RfqAwardResponse(rfq.Award.Id, rfq.Award.VendorId, rfq.Award.AmountFcy,
            rfq.Award.Currency, rfq.Award.SplitPercent, rfq.Award.Justification, rfq.Award.AwardedBy,
            rfq.Award.RequiresCfoApproval, rfq.Award.CfoApproved, rfq.Award.CfoApprovedBy));

    public static PurchaseOrderResponse ToPoResponse(PurchaseOrder po) => new(
        po.Id, po.TenantId, po.PoNumber, po.Source, po.VendorId, po.Currency, po.Incoterm, po.PaymentMode,
        po.LatestShipmentDate, po.PartialShipmentAllowed, po.TransshipmentAllowed, po.PsiRequired, po.CreatedBy,
        po.Status, po.PortOfLoading, po.PortOfDischarge, po.CfoOverrideReason, po.CloseReason,
        po.RevisionVersion, po.IsImport, po.TotalAmount,
        po.Lines.Select(l => new PoLineResponse(l.Id, l.ItemId, l.FreeText, l.HsCode, l.Quantity, l.Uom,
            l.UnitPrice, l.LineTotal, l.ReceivedQuantity, l.Notes)).ToList(),
        po.Feasibility is null ? null : new FeasibilitySnapshotResponse(po.Feasibility.Score,
            po.Feasibility.Verdict, po.Feasibility.Reasons, po.Feasibility.EvaluatedAtUtc),
        po.Revisions.Select(r => new PoRevisionResponse(r.Version, r.TotalDelta, r.Reason, r.By, r.AtUtc)).ToList());
}