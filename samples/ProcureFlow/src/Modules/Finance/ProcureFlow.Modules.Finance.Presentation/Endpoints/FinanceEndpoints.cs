using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Finance.Application.Commands;
using ProcureFlow.Modules.Finance.Application.Dtos;
using ProcureFlow.Modules.Finance.Application.Queries;
using ProcureFlow.Modules.Finance.Domain.Entities;
using ProcureFlow.Shared.Domain;
using CommandInvoiceLineDto = ProcureFlow.Modules.Finance.Application.Commands.InvoiceLineDto;
using CommandJournalLineDto = ProcureFlow.Modules.Finance.Application.Commands.JournalLineDto;

namespace ProcureFlow.Modules.Finance.Presentation;

// ── AP Invoices ────────────────────────────────────────────────────

internal sealed class CreateApInvoiceEndpoint : Endpoint<CreateApInvoiceEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateApInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ap-invoices");
        Tag(Tags.ApInvoice);
        Summary("Create an AP invoice (BR-FIN-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(new CreateApInvoiceCommand(
            req.InvoiceNumber, req.VendorId, req.InvoiceDate, req.DueDate, req.Currency,
            req.TotalAmount, req.Source, req.IsCreditNote,
            req.Lines.Select(l => new CommandInvoiceLineDto(
                l.PoLineId, l.GrnLineId, l.Description, l.Quantity, l.Uom, l.UnitPrice, l.LineTotal)).ToList()),
            ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public Guid VendorId { get; set; }
        public DateOnly InvoiceDate { get; set; }
        public DateOnly DueDate { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public ApInvoiceSource Source { get; set; }
        public bool IsCreditNote { get; set; }
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid PoLineId { get; set; }
        public Guid? GrnLineId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}

internal sealed class SubmitApInvoiceEndpoint : Endpoint<SubmitApInvoiceEndpoint.Request>
{
    private readonly IMediator _mediator;

    public SubmitApInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ap-invoices/{invoiceId}/submit");
        Tag(Tags.ApInvoice);
        Summary("Submit an AP invoice for approval (BR-FIN-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new SubmitApInvoiceCommand(req.InvoiceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid InvoiceId { get; set; }
    }
}

internal sealed class ApproveApInvoiceEndpoint : Endpoint<ApproveApInvoiceEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ApproveApInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ap-invoices/{invoiceId}/approve");
        Tag(Tags.ApInvoice);
        Summary("Approve an AP invoice (BR-FIN-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ApproveApInvoiceCommand(req.InvoiceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid InvoiceId { get; set; }
    }
}

internal sealed class CancelApInvoiceEndpoint : Endpoint<CancelApInvoiceEndpoint.Request>
{
    private readonly IMediator _mediator;

    public CancelApInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ap-invoices/{invoiceId}/cancel");
        Tag(Tags.ApInvoice);
        Summary("Cancel an AP invoice (BR-FIN-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new CancelApInvoiceCommand(req.InvoiceId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid InvoiceId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetApInvoiceByIdEndpoint : Endpoint<GetApInvoiceByIdEndpoint.Request, ApInvoiceDto>
{
    private readonly IMediator _mediator;

    public GetApInvoiceByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/ap-invoices/{invoiceId}");
        Tag(Tags.ApInvoice);
        Summary("Get an AP invoice by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        ApInvoiceDto? dto = await _mediator.QueryAsync<ApInvoiceDto?>(new GetApInvoiceByIdQuery(req.InvoiceId), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(dto, ct);
    }

    internal sealed class Request
    {
        public Guid InvoiceId { get; set; }
    }
}

internal sealed class GetApInvoicesByVendorEndpoint : Endpoint<GetApInvoicesByVendorEndpoint.Request, IReadOnlyList<ApInvoiceDto>>
{
    private readonly IMediator _mediator;

    public GetApInvoicesByVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/ap-invoices/vendor/{vendorId}");
        Tag(Tags.ApInvoice);
        Summary("List AP invoices for a vendor");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<ApInvoiceDto> result = await _mediator.QueryAsync<IReadOnlyList<ApInvoiceDto>>(
            new GetApInvoicesByVendorQuery(req.VendorId), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
    }
}

internal sealed class GetOverdueInvoicesEndpoint : Endpoint<GetOverdueInvoicesEndpoint.Request, IReadOnlyList<ApInvoiceDto>>
{
    private readonly IMediator _mediator;

    public GetOverdueInvoicesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/ap-invoices/overdue");
        Tag(Tags.ApInvoice);
        Summary("List overdue approved AP invoices");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<ApInvoiceDto> result = await _mediator.QueryAsync<IReadOnlyList<ApInvoiceDto>>(
            new GetOverdueInvoicesQuery(req.AsOfDate), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly AsOfDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    }
}

// ── Payment Proposals ──────────────────────────────────────────────

internal sealed class CreatePaymentProposalEndpoint : Endpoint<CreatePaymentProposalEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreatePaymentProposalEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/payment-proposals");
        Tag(Tags.PaymentProposal);
        Summary("Create a payment proposal (BR-FIN-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(new CreatePaymentProposalCommand(
            req.ProposalNumber, req.PaymentDate, req.Currency, req.TotalAmount, req.InvoiceIds), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string ProposalNumber { get; set; } = string.Empty;
        public DateOnly PaymentDate { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public List<Guid> InvoiceIds { get; set; } = new();
    }
}

internal sealed class ApprovePaymentProposalEndpoint : Endpoint<ApprovePaymentProposalEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ApprovePaymentProposalEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/payment-proposals/{proposalId}/approve");
        Tag(Tags.PaymentProposal);
        Summary("Approve a payment proposal (BR-FIN-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ApprovePaymentProposalCommand(req.ProposalId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ProposalId { get; set; }
    }
}

internal sealed class ExportPaymentProposalEndpoint : Endpoint<ExportPaymentProposalEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ExportPaymentProposalEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/payment-proposals/{proposalId}/export");
        Tag(Tags.PaymentProposal);
        Summary("Export a payment proposal (BR-FIN-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ExportPaymentProposalCommand(req.ProposalId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ProposalId { get; set; }
    }
}

internal sealed class GetPaymentProposalByIdEndpoint : Endpoint<GetPaymentProposalByIdEndpoint.Request, PaymentProposalDto>
{
    private readonly IMediator _mediator;

    public GetPaymentProposalByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/payment-proposals/{proposalId}");
        Tag(Tags.PaymentProposal);
        Summary("Get a payment proposal by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        PaymentProposalDto? dto = await _mediator.QueryAsync<PaymentProposalDto?>(
            new GetPaymentProposalByIdQuery(req.ProposalId), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(dto, ct);
    }

    internal sealed class Request
    {
        public Guid ProposalId { get; set; }
    }
}

// ── FX Rates ───────────────────────────────────────────────────────

internal sealed class CreateFxRateEndpoint : Endpoint<CreateFxRateEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateFxRateEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/fx-rates");
        Tag(Tags.FxRate);
        Summary("Create an FX rate (BR-FIN-08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(new CreateFxRateCommand(
            req.EffectiveDate, req.FromCurrency, req.ToCurrency, req.Rate, req.Source, req.SourceReference), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public DateOnly EffectiveDate { get; set; }
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public decimal Rate { get; set; }
        public FxSource Source { get; set; }
        public string? SourceReference { get; set; }
    }
}

internal sealed class GetFxRateEndpoint : Endpoint<GetFxRateEndpoint.Request, FxRateDto>
{
    private readonly IMediator _mediator;

    public GetFxRateEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/fx-rates/current");
        Tag(Tags.FxRate);
        Summary("Get the effective FX rate for a currency pair");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        FxRateDto? dto = await _mediator.QueryAsync<FxRateDto?>(
            new GetFxRateQuery(req.FromCurrency, req.ToCurrency, req.AsOfDate), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(dto, ct);
    }

    internal sealed class Request
    {
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public DateOnly AsOfDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    }
}

internal sealed class GetFxRatesEndpoint : Endpoint<GetFxRatesEndpoint.Request, IReadOnlyList<FxRateDto>>
{
    private readonly IMediator _mediator;

    public GetFxRatesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/fx-rates");
        Tag(Tags.FxRate);
        Summary("List FX rates within a date range");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<FxRateDto> result = await _mediator.QueryAsync<IReadOnlyList<FxRateDto>>(
            new GetFxRatesQuery(req.FromDate, req.ToDate), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
    }
}

// ── Cost Centers ───────────────────────────────────────────────────

internal sealed class CreateCostCenterEndpoint : Endpoint<CreateCostCenterEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateCostCenterEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cost-centers");
        Tag(Tags.CostCenter);
        Summary("Create a cost center (BR-FIN-09)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new CreateCostCenterCommand(req.Code, req.Name, req.ParentId, req.IsActive), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
        public bool IsActive { get; set; }
    }
}

internal sealed class GetCostCentersEndpoint : Endpoint<GetCostCentersEndpoint.Request, IReadOnlyList<CostCenterDto>>
{
    private readonly IMediator _mediator;

    public GetCostCentersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/cost-centers");
        Tag(Tags.CostCenter);
        Summary("List cost centers");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<CostCenterDto> result = await _mediator.QueryAsync<IReadOnlyList<CostCenterDto>>(
            new GetCostCentersQuery(req.IncludeInactive), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public bool IncludeInactive { get; set; }
    }
}

// ── Journal Batches ────────────────────────────────────────────────

internal sealed class CreateJournalBatchEndpoint : Endpoint<CreateJournalBatchEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateJournalBatchEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/journal-batches");
        Tag(Tags.JournalBatch);
        Summary("Create a journal batch (BR-FIN-10)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(new CreateJournalBatchCommand(
            req.JournalNumber, req.PostingDate, req.Description, req.Currency,
            req.Lines.Select(l => new CommandJournalLineDto(
                l.AccountCode, l.AccountName, l.Description, l.Debit, l.Credit, l.CostCenterId)).ToList()),
            ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string JournalNumber { get; set; } = string.Empty;
        public DateOnly PostingDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public Guid? CostCenterId { get; set; }
    }
}

internal sealed class PostJournalBatchEndpoint : Endpoint<PostJournalBatchEndpoint.Request>
{
    private readonly IMediator _mediator;

    public PostJournalBatchEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/journal-batches/{journalId}/post");
        Tag(Tags.JournalBatch);
        Summary("Post a journal batch (BR-FIN-11)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new PostJournalBatchCommand(req.JournalId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid JournalId { get; set; }
    }
}

internal sealed class GetJournalBatchByIdEndpoint : Endpoint<GetJournalBatchByIdEndpoint.Request, JournalBatchDto>
{
    private readonly IMediator _mediator;

    public GetJournalBatchByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/journal-batches/{journalId}");
        Tag(Tags.JournalBatch);
        Summary("Get a journal batch by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        JournalBatchDto? dto = await _mediator.QueryAsync<JournalBatchDto?>(
            new GetJournalBatchByIdQuery(req.JournalId), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(dto, ct);
    }

    internal sealed class Request
    {
        public Guid JournalId { get; set; }
    }
}

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

internal sealed class GetMatchExceptionByIdEndpoint : Endpoint<GetMatchExceptionByIdEndpoint.Request, MatchExceptionDto>
{
    private readonly IMediator _mediator;

    public GetMatchExceptionByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/match-exceptions/{exceptionId}");
        Tag(Tags.ApInvoice);
        Summary("Get a match exception by ID (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        MatchExceptionDto? dto = await _mediator.QueryAsync<MatchExceptionDto?>(
            new GetMatchExceptionByIdQuery(req.ExceptionId), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        await SendOkAsync(dto, ct);
    }

    internal sealed class Request { public Guid ExceptionId { get; set; } }
}

internal sealed class GetMatchExceptionsByInvoiceEndpoint : Endpoint<GetMatchExceptionsByInvoiceEndpoint.Request, IReadOnlyList<MatchExceptionDto>>
{
    private readonly IMediator _mediator;

    public GetMatchExceptionsByInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/ap-invoices/{invoiceId}/match-exceptions");
        Tag(Tags.ApInvoice);
        Summary("Get match exceptions for an invoice (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<MatchExceptionDto> result = await _mediator.QueryAsync<IReadOnlyList<MatchExceptionDto>>(
            new GetMatchExceptionsByInvoiceQuery(req.InvoiceId), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request { public Guid InvoiceId { get; set; } }
}

internal sealed class GetOpenMatchExceptionsEndpoint : Endpoint<GetOpenMatchExceptionsEndpoint.Request, IReadOnlyList<MatchExceptionDto>>
{
    private readonly IMediator _mediator;

    public GetOpenMatchExceptionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/match-exceptions/open");
        Tag(Tags.ApInvoice);
        Summary("Get all open match exceptions (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<MatchExceptionDto> result = await _mediator.QueryAsync<IReadOnlyList<MatchExceptionDto>>(
            new GetOpenMatchExceptionsQuery(), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request { }
}

internal sealed class ApproveMatchExceptionEndpoint : Endpoint<ApproveMatchExceptionEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ApproveMatchExceptionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/match-exceptions/{exceptionId}/approve");
        Tag(Tags.ApInvoice);
        Summary("Approve a match exception (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ApproveMatchExceptionCommand(req.ExceptionId, req.Notes), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ExceptionId { get; set; }
        public string? Notes { get; set; }
    }
}

internal sealed class RejectMatchExceptionEndpoint : Endpoint<RejectMatchExceptionEndpoint.Request>
{
    private readonly IMediator _mediator;

    public RejectMatchExceptionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/match-exceptions/{exceptionId}/reject");
        Tag(Tags.ApInvoice);
        Summary("Reject a match exception (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RejectMatchExceptionCommand(req.ExceptionId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ExceptionId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class OverrideMatchExceptionEndpoint : Endpoint<OverrideMatchExceptionEndpoint.Request>
{
    private readonly IMediator _mediator;

    public OverrideMatchExceptionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/match-exceptions/{exceptionId}/override");
        Tag(Tags.ApInvoice);
        Summary("Override a match exception with reason (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new OverrideMatchExceptionCommand(req.ExceptionId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ExceptionId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

internal sealed class CreateGrIrAccrualEndpoint : Endpoint<CreateGrIrAccrualEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public CreateGrIrAccrualEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/gr-ir-accruals");
        Tag(Tags.ApInvoice);
        Summary("Create a GR/IR accrual from GRN posting (BR-FIN-13)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(new CreateGrIrAccrualCommand(
            req.GrnId, req.PoId, req.VendorId, req.GrnNumber, req.ReceivedOn,
            req.Amount, req.Currency), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid GrnId { get; set; }
        public Guid? PoId { get; set; }
        public Guid VendorId { get; set; }
        public string GrnNumber { get; set; } = string.Empty;
        public DateOnly ReceivedOn { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}

internal sealed class ClearGrIrAccrualEndpoint : Endpoint<ClearGrIrAccrualEndpoint.Request>
{
    private readonly IMediator _mediator;

    public ClearGrIrAccrualEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/gr-ir-accruals/{accrualId}/clear");
        Tag(Tags.ApInvoice);
        Summary("Clear a GR/IR accrual when invoice matches (BR-FIN-13)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ClearGrIrAccrualCommand(
            req.AccrualId, req.InvoiceId, req.ClearedOn), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid AccrualId { get; set; }
        public Guid InvoiceId { get; set; }
        public DateOnly ClearedOn { get; set; }
    }
}

internal sealed class GetGrIrAccrualByIdEndpoint : Endpoint<GetGrIrAccrualByIdEndpoint.Request, GrIrAccrualDto>
{
    private readonly IMediator _mediator;

    public GetGrIrAccrualByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/gr-ir-accruals/{accrualId}");
        Tag(Tags.ApInvoice);
        Summary("Get a GR/IR accrual by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        GrIrAccrualDto? dto = await _mediator.QueryAsync<GrIrAccrualDto?>(
            new GetGrIrAccrualByIdQuery(req.AccrualId), ct);
        if (dto is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        await SendOkAsync(dto, ct);
    }

    internal sealed class Request { public Guid AccrualId { get; set; } }
}

internal sealed class GetOpenGrIrAccrualsEndpoint : Endpoint<GetOpenGrIrAccrualsEndpoint.Request, IReadOnlyList<GrIrAccrualDto>>
{
    private readonly IMediator _mediator;

    public GetOpenGrIrAccrualsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/gr-ir-accruals/open");
        Tag(Tags.ApInvoice);
        Summary("Get all open GR/IR accruals");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<GrIrAccrualDto> result = await _mediator.QueryAsync<IReadOnlyList<GrIrAccrualDto>>(
            new GetOpenGrIrAccrualsQuery(), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request { }
}

internal sealed class GetGrIrAccrualsByVendorEndpoint : Endpoint<GetGrIrAccrualsByVendorEndpoint.Request, IReadOnlyList<GrIrAccrualDto>>
{
    private readonly IMediator _mediator;

    public GetGrIrAccrualsByVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/vendors/{vendorId}/gr-ir-accruals");
        Tag(Tags.ApInvoice);
        Summary("Get GR/IR accruals for a vendor");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<GrIrAccrualDto> result = await _mediator.QueryAsync<IReadOnlyList<GrIrAccrualDto>>(
            new GetGrIrAccrualsByVendorQuery(req.VendorId), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request { public Guid VendorId { get; set; } }
}

// ── 3-Way Match (BR-FIN-12) ────────────────────────────────────────

internal sealed class PerformThreeWayMatchEndpoint : Endpoint<PerformThreeWayMatchEndpoint.Request, ThreeWayMatchResult>
{
    private readonly IMediator _mediator;

    public PerformThreeWayMatchEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ap-invoices/{invoiceId}/match");
        Tag(Tags.ApInvoice);
        Summary("Perform 3-way match: PO vs GRN vs Invoice with tolerance checks (BR-FIN-12)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ThreeWayMatchResult> result = await _mediator.SendAsync(
            new PerformThreeWayMatchCommand(req.InvoiceId, req.LineInputs), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid InvoiceId { get; set; }
        public List<ThreeWayMatchLineInput> LineInputs { get; set; } = new();
    }
}