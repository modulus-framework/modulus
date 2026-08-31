using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Budgeting.Application.Budgets.Commands;
using TradeFlow.Modules.Budgeting.Application.Budgets.Dtos;
using TradeFlow.Modules.Budgeting.Application.Budgets.Queries;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Budgeting.Presentation.Budgets;

internal sealed class CreateBudgetEndpoint : Endpoint<CreateBudgetEndpoint.CreateBudgetRequest, CreateBudgetResponse>
{
    private readonly IMediator _mediator;

    public CreateBudgetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets");
        Tag(Tags.Budgets);
        Summary("Create a budget (BR-BUD-01)");
    }

    public override async Task HandleAsync(CreateBudgetRequest req, CancellationToken ct)
    {
        Result<CreateBudgetResponse> result = await _mediator.SendAsync(new CreateBudgetCommand(
            req.FiscalYear,
            req.CostCenterId,
            req.Category,
            req.ProjectId,
            req.Currency,
            req.Amount,
            req.BlockMode,
            req.BudgetOwnerId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/budgets/{result.Value.BudgetId}", ct);
    }

    internal sealed class CreateBudgetRequest
    {
        public int FiscalYear { get; set; }
        public Guid CostCenterId { get; set; }
        public string Category { get; set; } = string.Empty;
        public Guid? ProjectId { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public BudgetBlockMode BlockMode { get; set; }
        public Guid BudgetOwnerId { get; set; }
    }
}

internal sealed class GetBudgetByIdEndpoint : Endpoint<GetBudgetByIdEndpoint.Request, BudgetDetailResponse>
{
    private readonly IMediator _mediator;

    public GetBudgetByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/budgets/{budgetId}");
        Tag(Tags.Budgets);
        Summary("Get a budget by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BudgetDetailResponse> result = await _mediator.QueryAsync(new GetBudgetByIdQuery(req.BudgetId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
    }
}

internal sealed class GetAllBudgetsEndpoint : Endpoint<GetAllBudgetsEndpoint.Request, IReadOnlyList<BudgetResponse>>
{
    private readonly IMediator _mediator;

    public GetAllBudgetsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/budgets");
        Tag(Tags.Budgets);
        Summary("List budgets, optionally filtered by fiscal year, cost center or category");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<BudgetResponse>> result = await _mediator.QueryAsync(new GetAllBudgetsQuery(
            req.FiscalYear, req.CostCenterId, req.Category), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class Request
    {
        public int? FiscalYear { get; set; }
        public Guid? CostCenterId { get; set; }
        public string? Category { get; set; }
    }
}

internal sealed class RequestBudgetRevisionEndpoint : Endpoint<RequestBudgetRevisionEndpoint.Request, RequestBudgetRevisionResponse>
{
    private readonly IMediator _mediator;

    public RequestBudgetRevisionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/revisions");
        Tag(Tags.Budgets);
        Summary("Request a versioned budget revision (BR-BUD-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<RequestBudgetRevisionResponse> result = await _mediator.SendAsync(new RequestBudgetRevisionCommand(
            req.BudgetId, req.NewAmount, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public decimal NewAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ApproveBudgetRevisionEndpoint : Endpoint<ApproveBudgetRevisionEndpoint.Request, BudgetRevisionResponse>
{
    private readonly IMediator _mediator;

    public ApproveBudgetRevisionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/revisions/{revisionId}/approve");
        Tag(Tags.Budgets);
        Summary("Approve a budget revision (CFO, BR-BUD-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BudgetRevisionResponse> result = await _mediator.SendAsync(new ApproveBudgetRevisionCommand(
            req.BudgetId, req.RevisionId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public Guid RevisionId { get; set; }
    }
}

internal sealed class RejectBudgetRevisionEndpoint : Endpoint<RejectBudgetRevisionEndpoint.Request, object>
{
    private readonly IMediator _mediator;

    public RejectBudgetRevisionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/revisions/{revisionId}/reject");
        Tag(Tags.Budgets);
        Summary("Reject a budget revision (BR-BUD-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RejectBudgetRevisionCommand(
            req.BudgetId, req.RevisionId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public Guid RevisionId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

/// <summary>BR-BUD-06: budget utilization threshold alerts (80%/95%).</summary>
internal sealed class GetBudgetUtilizationAlertsEndpoint : Endpoint<GetBudgetUtilizationAlertsEndpoint.Request, IReadOnlyList<BudgetUtilizationAlertResponse>>
{
    private readonly IMediator _mediator;

    public GetBudgetUtilizationAlertsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/budgets/utilization-alerts");
        Tag(Tags.Budgets);
        Summary("Get budgets exceeding utilization threshold (80%/95%, BR-BUD-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<BudgetUtilizationAlertResponse>> result = await _mediator.QueryAsync(
            new GetBudgetUtilizationAlertsQuery(req.FiscalYear, req.ThresholdPercent), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public int? FiscalYear { get; set; }
        public decimal ThresholdPercent { get; set; } = 80m;
    }
}

// ── Budget Lifecycle Endpoints (BR-BUD-02/04/05) ────────────────────

internal sealed class ReserveBudgetEndpoint : Endpoint<ReserveBudgetEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ReserveBudgetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/reserve");
        Tag(Tags.Budgets);
        Summary("Reserve budget funds (BR-BUD-02/04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ReserveBudgetCommand(
            req.BudgetId, req.Amount, req.SourceDocumentType,
            req.SourceDocumentNumber, req.ReferenceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public decimal Amount { get; set; }
        public string SourceDocumentType { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
    }
}

internal sealed class CommitBudgetEndpoint : Endpoint<CommitBudgetEndpoint.Request>
{
    private readonly IMediator _mediator;

    public CommitBudgetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/commit");
        Tag(Tags.Budgets);
        Summary("Convert reservation to commitment at PO approval (BR-BUD-02/05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new CommitBudgetCommand(
            req.BudgetId, req.Amount, req.SourceDocumentType,
            req.SourceDocumentNumber, req.ReferenceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public decimal Amount { get; set; }
        public string SourceDocumentType { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
    }
}

internal sealed class ConsumeBudgetEndpoint : Endpoint<ConsumeBudgetEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ConsumeBudgetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/consume");
        Tag(Tags.Budgets);
        Summary("Consume budget at GRN/invoice (BR-BUD-02/05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ConsumeBudgetCommand(
            req.BudgetId, req.Amount, req.SourceDocumentType,
            req.SourceDocumentNumber, req.ReferenceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public decimal Amount { get; set; }
        public string SourceDocumentType { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
    }
}

internal sealed class ReleaseBudgetEndpoint : Endpoint<ReleaseBudgetEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ReleaseBudgetEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/budgets/{budgetId}/release");
        Tag(Tags.Budgets);
        Summary("Release budget on cancellation (BR-BUD-02/05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ReleaseBudgetCommand(
            req.BudgetId, req.Amount, req.SourceDocumentType,
            req.SourceDocumentNumber, req.ReferenceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BudgetId { get; set; }
        public decimal Amount { get; set; }
        public string SourceDocumentType { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
    }
}