using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Inventory.Application.Commands;
using ProcureFlow.Modules.Inventory.Application.Dtos;
using ProcureFlow.Modules.Inventory.Application.Queries;
using ProcureFlow.Modules.Inventory.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Inventory.Presentation;

internal sealed class CreateStockItemEndpoint : Endpoint<CreateStockItemEndpoint.Request, StockItemResponse>
{
    private readonly IMediator _mediator;

    public CreateStockItemEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/stock-items");
        Tag(Tags.Stock);
        Summary("Create a stock item (BR-VAL-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<StockItemResponse> result = await _mediator.SendAsync(
            new CreateStockItemCommand(req.SiteId, req.ItemId, req.Sku, req.Name, req.Uom), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Uom { get; set; } = string.Empty;
    }
}

internal sealed class GetStockItemEndpoint : Endpoint<GetStockItemEndpoint.Request, StockItemResponse>
{
    private readonly IMediator _mediator;

    public GetStockItemEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/stock-items/{siteId}/{itemId}");
        Tag(Tags.Stock);
        Summary("Get a stock item with weighted-average valuation");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<StockItemResponse> result = await _mediator.QueryAsync(
            new GetStockItemQuery(req.SiteId, req.ItemId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
    }
}

internal sealed class RevalueStockEndpoint : Endpoint<RevalueStockEndpoint.Request, StockItemResponse>
{
    private readonly IMediator _mediator;

    public RevalueStockEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/stock-items/{siteId}/{itemId}/revalue");
        Tag(Tags.Stock);
        Summary("Revalue on-hand at LandedCostFinalized — posts COGS delta (BR-VAL-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<StockItemResponse> result = await _mediator.SendAsync(
            new RevalueStockCommand(req.SiteId, req.ItemId, req.NewUnitCost, req.Reference), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
        public decimal NewUnitCost { get; set; }
        public string Reference { get; set; } = string.Empty;
    }
}

internal sealed class ReceiveGoodsEndpoint : Endpoint<ReceiveGoodsEndpoint.Request, GrnResponse>
{
    private readonly IMediator _mediator;

    public ReceiveGoodsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/grns");
        Tag(Tags.Grns);
        Summary("Create a GRN with provisional-cost receipt (BR-GRN-01/04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<GrnResponse> result = await _mediator.SendAsync(new ReceiveGoodsCommand(
            req.FileId, req.PoId, req.VendorId, req.GrnNumber, req.ReceivedOn,
            req.Lines.Select(l => new ReceiveGoodsLineInput(l.ItemId, l.OrderedQty, l.ReceivedQty,
                l.OverReceiptTolerancePct, l.ProvisionalUnitCost, l.SourceDocNumber)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid? PoId { get; set; }
        public Guid? VendorId { get; set; }
        public string GrnNumber { get; set; } = string.Empty;
        public DateOnly ReceivedOn { get; set; }
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid ItemId { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal OverReceiptTolerancePct { get; set; }
        public decimal ProvisionalUnitCost { get; set; }
        public string SourceDocNumber { get; set; } = string.Empty;
    }
}

internal sealed class PostGrnEndpoint : Endpoint<PostGrnEndpoint.Request, GrnResponse>
{
    private readonly IMediator _mediator;

    public PostGrnEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/grns/{grnId}/post");
        Tag(Tags.Grns);
        Summary("Post the GRN — updates stock + value ledger");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<GrnResponse> result = await _mediator.SendAsync(new PostGrnCommand(req.GrnId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
    }
}

internal sealed class GetGrnEndpoint : Endpoint<GetGrnEndpoint.Request, GrnResponse>
{
    private readonly IMediator _mediator;

    public GetGrnEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/grns/{grnId}");
        Tag(Tags.Grns);
        Summary("Get a GRN by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<GrnResponse> result = await _mediator.QueryAsync(new GetGrnQuery(req.GrnId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
    }
}

internal sealed class CreateQcInspectionEndpoint : Endpoint<CreateQcInspectionEndpoint.Request, QcInspectionResponse>
{
    private readonly IMediator _mediator;

    public CreateQcInspectionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/qc-inspections");
        Tag(Tags.QualityControl);
        Summary("Create a QC inspection against a GRN");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<QcInspectionResponse> result = await _mediator.SendAsync(new CreateQcInspectionCommand(
            req.GrnId, req.InspectedOn, req.InspectedBy,
            req.Lines.Select(l => new QcInspectionLineInput(l.GrnLineId, l.ItemId, l.InspectedQty,
                l.AcceptedQty, l.Decision, l.Note)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
        public DateOnly InspectedOn { get; set; }
        public string InspectedBy { get; set; } = string.Empty;
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid GrnLineId { get; set; }
        public Guid ItemId { get; set; }
        public decimal InspectedQty { get; set; }
        public decimal AcceptedQty { get; set; }
        public QcDecision Decision { get; set; }
        public string? Note { get; set; }
    }
}

internal sealed class GetQcInspectionEndpoint : Endpoint<GetQcInspectionEndpoint.Request, QcInspectionResponse>
{
    private readonly IMediator _mediator;

    public GetQcInspectionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/qc-inspections/{grnId}");
        Tag(Tags.QualityControl);
        Summary("Get QC inspection for a GRN");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<QcInspectionResponse> result = await _mediator.QueryAsync(new GetQcInspectionQuery(req.GrnId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
    }
}

internal sealed class CreateBatchEndpoint : Endpoint<CreateBatchEndpoint.Request, BatchResponse>
{
    private readonly IMediator _mediator;

    public CreateBatchEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/batches");
        Tag(Tags.Batches);
        Summary("Create a batch/lot (imports default to file no, BR-VAL-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BatchResponse> result = await _mediator.SendAsync(new CreateBatchCommand(
            req.SiteId, req.ItemId, req.BatchNo, req.SourceDoc, req.Quantity, req.ExpiryDate, req.UnitCost), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public string? SourceDoc { get; set; }
        public decimal Quantity { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal UnitCost { get; set; }
    }
}

internal sealed class ListBatchesEndpoint : Endpoint<ListBatchesEndpoint.Request, IReadOnlyList<BatchResponse>>
{
    private readonly IMediator _mediator;

    public ListBatchesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/batches/{siteId}/{itemId}");
        Tag(Tags.Batches);
        Summary("List batches for an item (FEFO order)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<BatchResponse>> result = await _mediator.QueryAsync(
            new ListBatchesQuery(req.SiteId, req.ItemId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
    }
}

internal sealed class ListLedgerEntriesEndpoint : Endpoint<ListLedgerEntriesEndpoint.Request, IReadOnlyList<InventoryValueLedgerEntryResponse>>
{
    private readonly IMediator _mediator;

    public ListLedgerEntriesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/value-ledger/{siteId}/{itemId}");
        Tag(Tags.Stock);
        Summary("List append-only value-ledger entries for an item (BR-VAL-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<InventoryValueLedgerEntryResponse>> result = await _mediator.QueryAsync(
            new ListLedgerEntriesQuery(req.SiteId, req.ItemId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SiteId { get; set; }
        public Guid ItemId { get; set; }
    }
}

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

internal sealed class CreateReturnDraftEndpoint : Endpoint<CreateReturnDraftEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateReturnDraftEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/grn-return-drafts");
        Tag(Tags.Grn);
        Summary("Create a return/debit-note draft for QC rejected items (BR-GRN-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new CreateReturnDraftCommand(req.GrnId, req.CreatedOn, req.Lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
        public DateOnly CreatedOn { get; set; }
        public List<ReturnDraftLineInput> Lines { get; set; } = new();
    }
}

internal sealed class SubmitReturnDraftEndpoint : Endpoint<SubmitReturnDraftEndpoint.Request>
{
    private readonly IMediator _mediator;

    public SubmitReturnDraftEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/grn-return-drafts/{draftId}/submit");
        Tag(Tags.Grn);
        Summary("Submit a return draft and generate debit-note reference");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(
            new SubmitReturnDraftCommand(req.DraftId, req.DebitNoteNumber), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid DraftId { get; set; }
        public string DebitNoteNumber { get; set; } = string.Empty;
    }
}

internal sealed class GetReturnDraftEndpoint : Endpoint<GetReturnDraftEndpoint.Request, GrnReturnDraftResponse>
{
    private readonly IMediator _mediator;

    public GetReturnDraftEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/grn-return-drafts/{draftId}");
        Tag(Tags.Grn);
        Summary("Get a return draft by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<GrnReturnDraftResponse> result = await _mediator.QueryAsync(
            new GetReturnDraftQuery(req.DraftId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid DraftId { get; set; }
    }
}

internal sealed class ListReturnDraftsByGrnEndpoint : Endpoint<ListReturnDraftsByGrnEndpoint.Request, IReadOnlyList<GrnReturnDraftResponse>>
{
    private readonly IMediator _mediator;

    public ListReturnDraftsByGrnEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/grns/{grnId}/return-drafts");
        Tag(Tags.Grn);
        Summary("List return drafts for a GRN");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<GrnReturnDraftResponse>> result = await _mediator.QueryAsync(
            new ListReturnDraftsByGrnQuery(req.GrnId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
    }
}