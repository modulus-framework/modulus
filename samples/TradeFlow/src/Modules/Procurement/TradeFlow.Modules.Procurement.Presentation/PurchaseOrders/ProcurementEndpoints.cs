using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Procurement.Application.Commands;
using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Modules.Procurement.Application.Queries;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Presentation.PurchaseOrders;

internal sealed class CreatePrEndpoint : Endpoint<CreatePrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public CreatePrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/prs");
        Tag(Tags.Requisitions);
        Summary("Create a purchase requisition with lines (BR-PR-01/03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var lines = req.Lines.Select(l => new PrLineInput(l.ItemId, l.FreeText, l.Category, l.Quantity,
            l.Uom, l.NeedByDate, l.SuggestedVendorId, l.EstimatedUnitPrice, l.Currency, l.Notes)).ToList();

        Result<PurchaseRequisitionResponse> result = await _mediator.SendAsync(new CreatePrCommand(req.PrNumber, lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string PrNumber { get; set; } = string.Empty;
        public List<Line> Lines { get; set; } = new();
    }

    internal sealed class Line
    {
        public Guid? ItemId { get; set; }
        public string? FreeText { get; set; }
        public string? Category { get; set; }
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public DateOnly NeedByDate { get; set; }
        public Guid? SuggestedVendorId { get; set; }
        public decimal EstimatedUnitPrice { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}

internal sealed class SubmitPrEndpoint : Endpoint<SubmitPrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public SubmitPrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/prs/{prId}/submit");
        Tag(Tags.Requisitions);
        Summary("Submit a PR — budget availability check with soft block (BR-PR-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseRequisitionResponse> result = await _mediator.SendAsync(new SubmitPrCommand(
            req.PrId, req.CostCenterId, req.FiscalYear, req.CategoryLeadTimeDays), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PrId { get; set; }
        public Guid CostCenterId { get; set; }
        public int FiscalYear { get; set; }
        public int CategoryLeadTimeDays { get; set; }
    }
}

internal sealed class ApprovePrEndpoint : Endpoint<ApprovePrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public ApprovePrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/prs/{prId}/approve");
        Tag(Tags.Requisitions);
        Summary("Approve a PR — reserves budget (BR-PR-02/06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseRequisitionResponse> result = await _mediator.SendAsync(new ApprovePrCommand(req.PrId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PrId { get; set; }
    }
}

internal sealed class RejectPrEndpoint : Endpoint<RejectPrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public RejectPrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/prs/{prId}/reject");
        Tag(Tags.Requisitions);
        Summary("Reject a PR");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseRequisitionResponse> result = await _mediator.SendAsync(new RejectPrCommand(req.PrId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PrId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class CancelPrEndpoint : Endpoint<CancelPrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public CancelPrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/prs/{prId}/cancel");
        Tag(Tags.Requisitions);
        Summary("Cancel a PR — releases the budget reservation (BR-PR-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseRequisitionResponse> result = await _mediator.SendAsync(new CancelPrCommand(req.PrId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PrId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetPrEndpoint : Endpoint<GetPrEndpoint.Request, PurchaseRequisitionResponse>
{
    private readonly IMediator _mediator;

    public GetPrEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/prs/{prId}");
        Tag(Tags.Requisitions);
        Summary("Get a PR by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseRequisitionResponse> result = await _mediator.QueryAsync(new GetPrQuery(req.PrId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PrId { get; set; }
    }
}

internal sealed class ListPrsEndpoint : Endpoint<ListPrsEndpoint.Request, IReadOnlyList<PurchaseRequisitionResponse>>
{
    private readonly IMediator _mediator;

    public ListPrsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/prs");
        Tag(Tags.Requisitions);
        Summary("List PRs");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<PurchaseRequisitionResponse>> result = await _mediator.QueryAsync(new ListPrsQuery(req.FiscalYear, req.CostCenterId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public int? FiscalYear { get; set; }
        public Guid? CostCenterId { get; set; }
    }
}

internal sealed class CreateRfqEndpoint : Endpoint<CreateRfqEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public CreateRfqEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs");
        Tag(Tags.Sourcing);
        Summary("Create a sourcing case (RFQ) with AVL-enforced invitations (BR-SRC-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var lines = req.Lines.Select(l => new RfqLineInput(l.PrLineId, l.ItemId, l.FreeText, l.HsCode,
            l.Quantity, l.Uom, l.PortOfLoading, l.PortOfDischarge)).ToList();

        Result<RfqResponse> result = await _mediator.SendAsync(new CreateRfqCommand(
            req.RfqNumber, req.Title, req.IsSealed, req.DeadlineUtc, req.MinBidders, req.Currency,
            lines, req.InvitedVendorIds), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string RfqNumber { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsSealed { get; set; }
        public DateTime DeadlineUtc { get; set; }
        public int MinBidders { get; set; }
        public string Currency { get; set; } = string.Empty;
        public List<Line> Lines { get; set; } = new();
        public List<Guid> InvitedVendorIds { get; set; } = new();
    }

    internal sealed class Line
    {
        public Guid? PrLineId { get; set; }
        public Guid? ItemId { get; set; }
        public string? FreeText { get; set; }
        public string? HsCode { get; set; }
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public string? PortOfLoading { get; set; }
        public string? PortOfDischarge { get; set; }
    }
}

internal sealed class OpenRfqEndpoint : Endpoint<OpenRfqEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public OpenRfqEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/open");
        Tag(Tags.Sourcing);
        Summary("Open an RFQ for bidding");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new OpenRfqCommand(req.RfqId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
    }
}

internal sealed class SubmitBidEndpoint : Endpoint<SubmitBidEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public SubmitBidEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/bids");
        Tag(Tags.Sourcing);
        Summary("Submit a bid — late bids flagged (BR-SRC-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new SubmitBidCommand(
            req.RfqId, req.VendorId, req.BidNo, req.TotalAmountFcy, req.Currency, req.Notes), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
        public Guid VendorId { get; set; }
        public string BidNo { get; set; } = string.Empty;
        public decimal TotalAmountFcy { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}

internal sealed class ComputeRfqComparisonEndpoint : Endpoint<ComputeRfqComparisonEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public ComputeRfqComparisonEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/compare");
        Tag(Tags.Sourcing);
        Summary("Compute the landed-cost-normalized bid tab (BR-SRC-05/07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new ComputeRfqComparisonCommand(
            req.RfqId, req.Category, req.FreightPctOfFob, req.HandlingPctOfFob, req.CustomsFxRate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
        public string Category { get; set; } = string.Empty;
        public decimal FreightPctOfFob { get; set; }
        public decimal HandlingPctOfFob { get; set; }
        public decimal CustomsFxRate { get; set; }
    }
}

internal sealed class AwardRfqEndpoint : Endpoint<AwardRfqEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public AwardRfqEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/award");
        Tag(Tags.Sourcing);
        Summary("Award an RFQ — non-lowest awards need justification + CFO (BR-SRC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new AwardRfqCommand(
            req.RfqId, req.VendorId, req.AmountFcy, req.SplitPercent, req.Justification), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
        public Guid VendorId { get; set; }
        public decimal AmountFcy { get; set; }
        public decimal SplitPercent { get; set; }
        public string Justification { get; set; } = string.Empty;
    }
}

internal sealed class ApproveRfqAwardEndpoint : Endpoint<ApproveRfqAwardEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public ApproveRfqAwardEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/award/approve-cfo");
        Tag(Tags.Sourcing);
        Summary("CFO co-approval for a non-lowest award (BR-SRC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new ApproveRfqAwardCommand(req.RfqId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
    }
}

internal sealed class CancelRfqEndpoint : Endpoint<CancelRfqEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public CancelRfqEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/rfqs/{rfqId}/cancel");
        Tag(Tags.Sourcing);
        Summary("Cancel an RFQ");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.SendAsync(new CancelRfqCommand(req.RfqId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetRfqEndpoint : Endpoint<GetRfqEndpoint.Request, RfqResponse>
{
    private readonly IMediator _mediator;

    public GetRfqEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/rfqs/{rfqId}");
        Tag(Tags.Sourcing);
        Summary("Get an RFQ with bids, comparison and award");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RfqResponse> result = await _mediator.QueryAsync(new GetRfqQuery(req.RfqId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RfqId { get; set; }
    }
}

internal sealed class CreatePoEndpoint : Endpoint<CreatePoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public CreatePoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos");
        Tag(Tags.PurchaseOrders);
        Summary("Create a purchase order (award / PR direct / manual — BR-PO-01/02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var lines = req.Lines.Select(l => new PoLineInput(l.ItemId, l.FreeText, l.HsCode, l.Quantity,
            l.Uom, l.UnitPrice, l.Notes)).ToList();

        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new CreatePoCommand(
            req.PoNumber, req.Source, req.VendorId, req.Currency, req.Incoterm, req.PaymentMode,
            req.LatestShipmentDate, req.PartialShipmentAllowed, req.TransshipmentAllowed, req.PsiRequired,
            req.PortOfLoading, req.PortOfDischarge, req.ShipmentTolerancePct, req.ReceivedTolerancePct,
            req.RfqId, lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string PoNumber { get; set; } = string.Empty;
        public PoSource Source { get; set; }
        public Guid VendorId { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Incoterm { get; set; } = string.Empty;
        public PaymentMode PaymentMode { get; set; }
        public DateOnly? LatestShipmentDate { get; set; }
        public bool PartialShipmentAllowed { get; set; }
        public bool TransshipmentAllowed { get; set; }
        public bool PsiRequired { get; set; }
        public string? PortOfLoading { get; set; }
        public string? PortOfDischarge { get; set; }
        public decimal ShipmentTolerancePct { get; set; }
        public decimal ReceivedTolerancePct { get; set; }
        public Guid? RfqId { get; set; }
        public List<Line> Lines { get; set; } = new();
    }

    internal sealed class Line
    {
        public Guid? ItemId { get; set; }
        public string? FreeText { get; set; }
        public string? HsCode { get; set; }
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}

internal sealed class SubmitPoEndpoint : Endpoint<SubmitPoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public SubmitPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/submit");
        Tag(Tags.PurchaseOrders);
        Summary("Submit a PO — feasibility gate with snapshot (BR-PO-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new SubmitPoCommand(
            req.PoId, req.BudgetFiscalYear, req.BudgetCostCenterId, req.BudgetCategory), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public int BudgetFiscalYear { get; set; }
        public Guid BudgetCostCenterId { get; set; }
        public string BudgetCategory { get; set; } = string.Empty;
    }
}

internal sealed class RecordCfoOverrideEndpoint : Endpoint<RecordCfoOverrideEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public RecordCfoOverrideEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/cfo-override");
        Tag(Tags.PurchaseOrders);
        Summary("Record a CFO override for a blocked import PO (BR-PO-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new RecordCfoOverrideCommand(req.PoId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ApprovePoEndpoint : Endpoint<ApprovePoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public ApprovePoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/approve");
        Tag(Tags.PurchaseOrders);
        Summary("Approve a PO — budget moves reserve to commitment (BR-PO-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new ApprovePoCommand(req.PoId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
    }
}

internal sealed class DispatchPoEndpoint : Endpoint<DispatchPoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public DispatchPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/dispatch");
        Tag(Tags.PurchaseOrders);
        Summary("Dispatch a PO to the vendor (BR-PO-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new DispatchPoCommand(req.PoId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
    }
}

internal sealed class ReceivePoEndpoint : Endpoint<ReceivePoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public ReceivePoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/receive");
        Tag(Tags.PurchaseOrders);
        Summary("Record receipt — auto-close when fully received (BR-PO-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new ReceivePoCommand(req.PoId, req.LineId, req.Quantity), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public Guid LineId { get; set; }
        public decimal Quantity { get; set; }
    }
}

internal sealed class RevisePoEndpoint : Endpoint<RevisePoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public RevisePoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/revise");
        Tag(Tags.PurchaseOrders);
        Summary("Revise a PO — value-increasing revisions re-enter approval (BR-PO-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new RevisePoCommand(req.PoId, req.NewTotalDelta, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public decimal NewTotalDelta { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ForceClosePoEndpoint : Endpoint<ForceClosePoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public ForceClosePoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/force-close");
        Tag(Tags.PurchaseOrders);
        Summary("Force-close a PO — releases residual commitment (BR-PO-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new ForceClosePoCommand(req.PoId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class CancelPoEndpoint : Endpoint<CancelPoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public CancelPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/pos/{poId}/cancel");
        Tag(Tags.PurchaseOrders);
        Summary("Cancel a PO — releases commitment");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.SendAsync(new CancelPoCommand(req.PoId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetPoEndpoint : Endpoint<GetPoEndpoint.Request, PurchaseOrderResponse>
{
    private readonly IMediator _mediator;

    public GetPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/pos/{poId}");
        Tag(Tags.PurchaseOrders);
        Summary("Get a PO with feasibility snapshot and revisions");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PurchaseOrderResponse> result = await _mediator.QueryAsync(new GetPoQuery(req.PoId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PoId { get; set; }
    }
}

internal sealed class ListPosEndpoint : Endpoint<ListPosEndpoint.Request, IReadOnlyList<PurchaseOrderResponse>>
{
    private readonly IMediator _mediator;

    public ListPosEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/pos");
        Tag(Tags.PurchaseOrders);
        Summary("List POs");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<PurchaseOrderResponse>> result = await _mediator.QueryAsync(new ListPosQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}