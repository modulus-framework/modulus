using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Costing.Application.Commands;
using ProcureFlow.Modules.Costing.Application.Dtos;
using ProcureFlow.Modules.Costing.Application.Queries;
using ProcureFlow.Modules.Costing.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Costing.Presentation;

internal sealed class CreateLandedCostSheetEndpoint : Endpoint<CreateLandedCostSheetEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public CreateLandedCostSheetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-sheets");
        Tag(Tags.LandedCost);
        Summary("Create a landed cost sheet for an import file (BR-LCS-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.SendAsync(new CreateLandedCostSheetCommand(
            req.FileId, req.SheetNumber, req.Currency,
            req.Lines.Select(l => new CostSheetLineInput(l.SourceLineId, l.GoodsValueFcy, l.GoodsValueBdt,
                l.ReceivedQty, l.NetWeightKg, l.GrossWeightKg, l.VolumeCbm, l.ContainerShare)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string SheetNumber { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid SourceLineId { get; set; }
        public decimal GoodsValueFcy { get; set; }
        public decimal GoodsValueBdt { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal NetWeightKg { get; set; }
        public decimal GrossWeightKg { get; set; }
        public decimal VolumeCbm { get; set; }
        public decimal ContainerShare { get; set; }
    }
}

internal sealed class GetLandedCostSheetEndpoint : Endpoint<GetLandedCostSheetEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public GetLandedCostSheetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/cost-sheets/{sheetId}");
        Tag(Tags.LandedCost);
        Summary("Get a landed cost sheet by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.QueryAsync(new GetLandedCostSheetQuery(req.SheetId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SheetId { get; set; }
    }
}

internal sealed class GetLandedCostSheetByFileEndpoint : Endpoint<GetLandedCostSheetByFileEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public GetLandedCostSheetByFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/cost-sheets/file/{fileId}");
        Tag(Tags.LandedCost);
        Summary("Get the landed cost sheet for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.QueryAsync(new GetLandedCostSheetByFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class ListLandedCostSheetsEndpoint : Endpoint<ListLandedCostSheetsEndpoint.Request, IReadOnlyList<LandedCostSheetResponse>>
{
    private readonly IMediator _mediator;

    public ListLandedCostSheetsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/cost-sheets");
        Tag(Tags.LandedCost);
        Summary("List all landed cost sheets for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<LandedCostSheetResponse>> result = await _mediator.QueryAsync(new ListLandedCostSheetsQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class AddCostElementEndpoint : Endpoint<AddCostElementEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public AddCostElementEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-sheets/{sheetId}/elements");
        Tag(Tags.LandedCost);
        Summary("Stage a cost element with driver + scope (BR-LCS-06/10)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.SendAsync(new AddCostElementCommand(
            req.SheetId, req.Name, req.AmountFcy, req.FxRate, req.AmountBdt, req.Driver, req.Scope,
            req.Treatment, req.SourceDocType, req.SourceDocNumber, req.SelectedLineIds), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SheetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal AmountFcy { get; set; }
        public decimal FxRate { get; set; }
        public decimal AmountBdt { get; set; }
        public CostElementDriver Driver { get; set; }
        public CostElementScope Scope { get; set; }
        public CostTreatment Treatment { get; set; }
        public string SourceDocType { get; set; } = string.Empty;
        public string SourceDocNumber { get; set; } = string.Empty;
        public List<Guid>? SelectedLineIds { get; set; }
    }
}

internal sealed class AllocateCostsEndpoint : Endpoint<AllocateCostsEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public AllocateCostsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-sheets/{sheetId}/allocate");
        Tag(Tags.LandedCost);
        Summary("Run the allocation engine (banker's rounding + residual, BR-LCS-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.SendAsync(new AllocateCostsCommand(req.SheetId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SheetId { get; set; }
    }
}

internal sealed class FinalizeCostSheetEndpoint : Endpoint<FinalizeCostSheetEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public FinalizeCostSheetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-sheets/{sheetId}/finalize");
        Tag(Tags.LandedCost);
        Summary("Submit the sheet for finalization (BR-LCS-08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.SendAsync(new FinalizeCostSheetCommand(req.SheetId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SheetId { get; set; }
    }
}

internal sealed class OpenAdjustmentEndpoint : Endpoint<OpenAdjustmentEndpoint.Request, LandedCostSheetResponse>
{
    private readonly IMediator _mediator;

    public OpenAdjustmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-sheets/{sheetId}/adjust");
        Tag(Tags.LandedCost);
        Summary("Open an adjustment version for late bills (BR-LCS-09)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LandedCostSheetResponse> result = await _mediator.SendAsync(new OpenAdjustmentCommand(req.SheetId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid SheetId { get; set; }
    }
}