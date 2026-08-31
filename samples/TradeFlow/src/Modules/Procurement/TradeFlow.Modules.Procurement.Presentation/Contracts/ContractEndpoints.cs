using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Procurement.Application.Commands;
using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Modules.Procurement.Application.Queries;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Presentation.Contracts;

internal sealed class CreateContractEndpoint : Endpoint<CreateContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public CreateContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts");
        Tag(Tags.Contracts);
        Summary("Create a rate/framework/service contract");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var lines = req.Lines.Select(l => new ContractLineInput(
            l.ItemId, l.FreeText, l.UnitPrice, l.MinQuantity, l.EscalationJson, l.Notes)).ToList();

        Result<ContractResponse> result = await _mediator.SendAsync(new CreateContractCommand(
            req.ContractNumber, req.VendorId, req.Type, req.Currency,
            req.StartDate, req.EndDate, req.CapValue, req.Notes, lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string ContractNumber { get; set; } = string.Empty;
        public Guid VendorId { get; set; }
        public ContractType Type { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public decimal CapValue { get; set; }
        public string? Notes { get; set; }
        public List<Line> Lines { get; set; } = new();
    }

    internal sealed class Line
    {
        public Guid? ItemId { get; set; }
        public string? FreeText { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? MinQuantity { get; set; }
        public string? EscalationJson { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}

internal sealed class SubmitContractEndpoint : Endpoint<SubmitContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public SubmitContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/submit");
        Tag(Tags.Contracts);
        Summary("Submit a contract for approval");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new SubmitContractCommand(req.ContractId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request { public Guid ContractId { get; set; } }
}

internal sealed class ApproveContractEndpoint : Endpoint<ApproveContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public ApproveContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/approve");
        Tag(Tags.Contracts);
        Summary("Approve a contract");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new ApproveContractCommand(req.ContractId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request { public Guid ContractId { get; set; } }
}

internal sealed class RenewContractEndpoint : Endpoint<RenewContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public RenewContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/renew");
        Tag(Tags.Contracts);
        Summary("Renew a contract with redline diff (BR-CTR-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new RenewContractCommand(
            req.ContractId, req.NewEndDate, req.NewCapValue, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ContractId { get; set; }
        public DateOnly NewEndDate { get; set; }
        public decimal? NewCapValue { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class TerminateContractEndpoint : Endpoint<TerminateContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public TerminateContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/terminate");
        Tag(Tags.Contracts);
        Summary("Terminate a contract");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new TerminateContractCommand(req.ContractId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ContractId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class CancelContractEndpoint : Endpoint<CancelContractEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public CancelContractEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/cancel");
        Tag(Tags.Contracts);
        Summary("Cancel a draft contract");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new CancelContractCommand(req.ContractId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ContractId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class RecordContractConsumptionEndpoint : Endpoint<RecordContractConsumptionEndpoint.Request, ContractResponse>
{
    private readonly IMediator _mediator;

    public RecordContractConsumptionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/contracts/{contractId}/consumption");
        Tag(Tags.Contracts);
        Summary("Record consumption against a contract cap");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractResponse> result = await _mediator.SendAsync(new RecordContractConsumptionCommand(req.ContractId, req.Amount), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ContractId { get; set; }
        public decimal Amount { get; set; }
    }
}

internal sealed class GetContractByIdEndpoint : Endpoint<GetContractByIdEndpoint.Request, ContractDetailResponse>
{
    private readonly IMediator _mediator;

    public GetContractByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/contracts/{contractId}");
        Tag(Tags.Contracts);
        Summary("Get a contract with lines, documents, milestones, revisions");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ContractDetailResponse> result = await _mediator.QueryAsync(new GetContractByIdQuery(req.ContractId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request { public Guid ContractId { get; set; } }
}

internal sealed class ListContractsEndpoint : Endpoint<ListContractsEndpoint.Request, IReadOnlyList<ContractResponse>>
{
    private readonly IMediator _mediator;

    public ListContractsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/contracts");
        Tag(Tags.Contracts);
        Summary("List contracts with optional status/vendor filter");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ContractResponse>> result = await _mediator.QueryAsync(
            new ListContractsQuery(req.Status, req.VendorId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public ContractStatus? Status { get; set; }
        public Guid? VendorId { get; set; }
    }
}

internal sealed class GetExpiringContractsEndpoint : Endpoint<GetExpiringContractsEndpoint.Request, IReadOnlyList<ContractResponse>>
{
    private readonly IMediator _mediator;

    public GetExpiringContractsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/contracts/expiring");
        Tag(Tags.Contracts);
        Summary("Get contracts expiring within N days (default 60)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ContractResponse>> result = await _mediator.QueryAsync(
            new GetExpiringContractsQuery(req.WithinDays), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request { public int WithinDays { get; set; } = 60; }
}

internal sealed class CheckMaverickPurchaseEndpoint : Endpoint<CheckMaverickPurchaseEndpoint.Request, MaverickCheckResponse>
{
    private readonly IMediator _mediator;

    public CheckMaverickPurchaseEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/contracts/maverick-check");
        Tag(Tags.Contracts);
        Summary("Check if a vendor+item combination has an active contract (BR-CTR-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<MaverickCheckResponse> result = await _mediator.QueryAsync(
            new CheckMaverickPurchaseQuery(req.VendorId, req.ItemId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public Guid ItemId { get; set; }
    }
}
