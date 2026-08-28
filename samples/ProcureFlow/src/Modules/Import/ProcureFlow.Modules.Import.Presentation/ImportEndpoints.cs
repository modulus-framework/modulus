using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Import.Application.Commands;
using ProcureFlow.Modules.Import.Application.Dtos;
using ProcureFlow.Modules.Import.Application.Queries;
using ProcureFlow.Modules.Import.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Presentation;

internal sealed class CreateImportFileEndpoint : Endpoint<CreateImportFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public CreateImportFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files");
        Tag(Tags.ImportFiles);
        Summary("Create a new import file (BR-IMP-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new CreateImportFileCommand(
            req.CompanyId, req.FiscalYear, req.PoId, req.Incoterm, req.Currency,
            req.PortOfLoading, req.PortOfDischarge, req.EstimatedGoodsValue), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CompanyId { get; set; }
        public int FiscalYear { get; set; }
        public Guid? PoId { get; set; }
        public string Incoterm { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string PortOfLoading { get; set; } = string.Empty;
        public string PortOfDischarge { get; set; } = string.Empty;
        public decimal EstimatedGoodsValue { get; set; }
    }
}

internal sealed class GetImportFileEndpoint : Endpoint<GetImportFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public GetImportFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-files/{fileId}");
        Tag(Tags.ImportFiles);
        Summary("Get an import file by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.QueryAsync(new GetImportFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class ListImportFilesEndpoint : Endpoint<ListImportFilesEndpoint.Request, IReadOnlyList<ImportFileResponse>>
{
    private readonly IMediator _mediator;

    public ListImportFilesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-files");
        Tag(Tags.ImportFiles);
        Summary("List all import files for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ImportFileResponse>> result = await _mediator.QueryAsync(new ListImportFilesQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class LinkImportPoEndpoint : Endpoint<LinkImportPoEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public LinkImportPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/link-po");
        Tag(Tags.ImportFiles);
        Summary("Link a PO to the import file (BR-IF-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new LinkImportPoCommand(req.FileId, req.PoId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid PoId { get; set; }
    }
}

// ── Import Planning Endpoints (BR-IP-01..06) ────────────────────────

internal sealed class CreateImportPlanEndpoint : Endpoint<CreateImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public CreateImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans");
        Tag(Tags.ImportFiles);
        Summary("Create a new import plan (BR-IP-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<ImportPlanLineInput> lines = req.Lines.Select(l => new ImportPlanLineInput(
            l.ItemId, l.CategoryId, l.Description, l.EstQty, l.EstFob, l.EstLanded,
            l.TargetMonth, l.SourceCountry)).ToList();

        Result<ImportPlanResponse> result = await _mediator.SendAsync(new CreateImportPlanCommand(
            req.CompanyId, req.FiscalYear, req.PeriodStart, req.PeriodEnd,
            req.Currency, lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CompanyId { get; set; }
        public int FiscalYear { get; set; }
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public string Currency { get; set; } = string.Empty;
        public IReadOnlyList<PlanLineInput> Lines { get; set; } = [];
    }

    internal sealed class PlanLineInput
    {
        public Guid? ItemId { get; set; }
        public Guid? CategoryId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal EstQty { get; set; }
        public decimal EstFob { get; set; }
        public decimal EstLanded { get; set; }
        public decimal? TargetMonth { get; set; }
        public string? SourceCountry { get; set; }
    }
}

internal sealed class GetImportPlanEndpoint : Endpoint<GetImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public GetImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-plans/{planId}");
        Tag(Tags.ImportFiles);
        Summary("Get an import plan by ID (BR-IP-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.QueryAsync(new GetImportPlanQuery(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

internal sealed class ListImportPlansEndpoint : Endpoint<ListImportPlansEndpoint.Request, IReadOnlyList<ImportPlanResponse>>
{
    private readonly IMediator _mediator;

    public ListImportPlansEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-plans");
        Tag(Tags.ImportFiles);
        Summary("List all import plans for the current tenant (BR-IP-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ImportPlanResponse>> result = await _mediator.QueryAsync(new ListImportPlansQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class ListImportPlansByFiscalYearEndpoint : Endpoint<ListImportPlansByFiscalYearEndpoint.Request, IReadOnlyList<ImportPlanResponse>>
{
    private readonly IMediator _mediator;

    public ListImportPlansByFiscalYearEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-plans/fiscal-year/{fiscalYear}");
        Tag(Tags.ImportFiles);
        Summary("List import plans by fiscal year");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ImportPlanResponse>> result = await _mediator.QueryAsync(
            new ListImportPlansByFiscalYearQuery(req.FiscalYear), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public int FiscalYear { get; set; }
    }
}

internal sealed class AddImportPlanLineEndpoint : Endpoint<AddImportPlanLineEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public AddImportPlanLineEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/lines");
        Tag(Tags.ImportFiles);
        Summary("Add a line to an import plan (BR-IP-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(new AddImportPlanLineCommand(
            req.PlanId, req.ItemId, req.CategoryId, req.Description,
            req.EstQty, req.EstFob, req.EstLanded, req.TargetMonth, req.SourceCountry), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
        public Guid? ItemId { get; set; }
        public Guid? CategoryId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal EstQty { get; set; }
        public decimal EstFob { get; set; }
        public decimal EstLanded { get; set; }
        public decimal? TargetMonth { get; set; }
        public string? SourceCountry { get; set; }
    }
}

internal sealed class RemoveImportPlanLineEndpoint : Endpoint<RemoveImportPlanLineEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public RemoveImportPlanLineEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/import-plans/{planId}/lines/{lineId}");
        Tag(Tags.ImportFiles);
        Summary("Remove a line from an import plan (BR-IP-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(
            new RemoveImportPlanLineCommand(req.PlanId, req.LineId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
        public Guid LineId { get; set; }
    }
}

internal sealed class SubmitImportPlanEndpoint : Endpoint<SubmitImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public SubmitImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/submit");
        Tag(Tags.ImportFiles);
        Summary("Submit an import plan for approval (BR-IP-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(new SubmitImportPlanCommand(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

internal sealed class ApproveImportPlanEndpoint : Endpoint<ApproveImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public ApproveImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/approve");
        Tag(Tags.ImportFiles);
        Summary("Approve an import plan (CFO approval)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(new ApproveImportPlanCommand(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

internal sealed class ReviseImportPlanEndpoint : Endpoint<ReviseImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public ReviseImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/revise");
        Tag(Tags.ImportFiles);
        Summary("Revise an approved import plan (increments version)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(new ReviseImportPlanCommand(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

internal sealed class CloseImportPlanEndpoint : Endpoint<CloseImportPlanEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public CloseImportPlanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/close");
        Tag(Tags.ImportFiles);
        Summary("Close an import plan at period end");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(new CloseImportPlanCommand(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

internal sealed class RecordPlanActualsEndpoint : Endpoint<RecordPlanActualsEndpoint.Request, ImportPlanResponse>
{
    private readonly IMediator _mediator;

    public RecordPlanActualsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-plans/{planId}/actuals");
        Tag(Tags.ImportFiles);
        Summary("Record actuals against a plan line when a file closes (BR-IP-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPlanResponse> result = await _mediator.SendAsync(
            new RecordPlanActualsCommand(req.PlanId, req.LineId, req.Qty, req.Fob, req.Landed), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
        public Guid LineId { get; set; }
        public decimal Qty { get; set; }
        public decimal Fob { get; set; }
        public decimal Landed { get; set; }
    }
}

internal sealed class GetPlanAdherenceReportEndpoint : Endpoint<GetPlanAdherenceReportEndpoint.Request, PlanAdherenceReportResponse>
{
    private readonly IMediator _mediator;

    public GetPlanAdherenceReportEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-plans/{planId}/adherence");
        Tag(Tags.ImportFiles);
        Summary("Plan vs. actual adherence report (BR-IP-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PlanAdherenceReportResponse> result = await _mediator.QueryAsync(
            new GetPlanAdherenceReportQuery(req.PlanId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlanId { get; set; }
    }
}

// ── Bill of Entry Endpoints (BR-CC-01..05) ─────────────────────────

internal sealed class CreateBillOfEntryEndpoint : Endpoint<CreateBillOfEntryEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public CreateBillOfEntryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry");
        Tag(Tags.Documents);
        Summary("Create a Bill of Entry for customs clearance (BR-CC-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new CreateBillOfEntryCommand(
            req.FileId, req.BoeNumber, req.BoeDate, req.CustomsOffice, req.CnfAgentId,
            req.Lane, req.DeclarantAin,
            req.Lines.Select(l => new BoeLineInput(l.CiLineId, l.HsCode, l.AssessableValue, l.Quantity, l.Uom)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string BoeNumber { get; set; } = string.Empty;
        public DateOnly BoeDate { get; set; }
        public string CustomsOffice { get; set; } = string.Empty;
        public Guid? CnfAgentId { get; set; }
        public BoeLane Lane { get; set; }
        public string DeclarantAin { get; set; } = string.Empty;
        public List<BoeLineRequest> Lines { get; set; } = new();
    }

    internal sealed class BoeLineRequest
    {
        public Guid? CiLineId { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public decimal AssessableValue { get; set; }
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
    }
}

internal sealed class GetBillOfEntryEndpoint : Endpoint<GetBillOfEntryEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public GetBillOfEntryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/bills-of-entry/{boeId}");
        Tag(Tags.Documents);
        Summary("Get a Bill of Entry by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.QueryAsync(new GetBillOfEntryQuery(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class GetBillOfEntryByFileEndpoint : Endpoint<GetBillOfEntryByFileEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public GetBillOfEntryByFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-files/{fileId}/bill-of-entry");
        Tag(Tags.Documents);
        Summary("Get the Bill of Entry for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.QueryAsync(new GetBillOfEntryByFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class SubmitBillOfEntryEndpoint : Endpoint<SubmitBillOfEntryEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public SubmitBillOfEntryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/submit");
        Tag(Tags.Documents);
        Summary("Submit a draft Bill of Entry to customs");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new SubmitBillOfEntryCommand(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class RecordBoeQueryEndpoint : Endpoint<RecordBoeQueryEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public RecordBoeQueryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/query");
        Tag(Tags.Documents);
        Summary("Record a customs query on the BoE");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new RecordBoeQueryCommand(req.BoeId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class RecordBoeAssessmentEndpoint : Endpoint<RecordBoeAssessmentEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public RecordBoeAssessmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/assess");
        Tag(Tags.Documents);
        Summary("Record customs assessment of the BoE");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new RecordBoeAssessmentCommand(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class RecordBoePaymentEndpoint : Endpoint<RecordBoePaymentEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public RecordBoePaymentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/pay");
        Tag(Tags.Documents);
        Summary("Record duty payment for the BoE");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new RecordBoePaymentCommand(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class RecordBoeExaminationEndpoint : Endpoint<RecordBoeExaminationEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public RecordBoeExaminationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/examine");
        Tag(Tags.Documents);
        Summary("Record customs examination and assign lane (G/Y/R)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new RecordBoeExaminationCommand(req.BoeId, req.Lane), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public BoeLane Lane { get; set; }
    }
}

internal sealed class ReleaseBillOfEntryEndpoint : Endpoint<ReleaseBillOfEntryEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public ReleaseBillOfEntryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/release");
        Tag(Tags.Documents);
        Summary("Release the BoE after customs clearance");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(new ReleaseBillOfEntryCommand(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class AddBoeDutyLineEndpoint : Endpoint<AddBoeDutyLineEndpoint.Request, BillOfEntryResponse>
{
    private readonly IMediator _mediator;

    public AddBoeDutyLineEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/bills-of-entry/{boeId}/duty-lines");
        Tag(Tags.Documents);
        Summary("Add a duty line to the BoE");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BillOfEntryResponse> result = await _mediator.SendAsync(
            new AddBoeDutyLineCommand(req.BoeId, req.Component, req.Rate, req.Amount, req.SroRef), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public string Component { get; set; } = string.Empty;
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public string? SroRef { get; set; }
    }
}

// ── Assessment Variance Endpoints (BR-CC-03) ───────────────────────

internal sealed class CreateAssessmentVarianceEndpoint : Endpoint<CreateAssessmentVarianceEndpoint.Request, AssessmentVarianceResponse>
{
    private readonly IMediator _mediator;

    public CreateAssessmentVarianceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/assessment-variances");
        Tag(Tags.Documents);
        Summary("Create an assessment variance between system and customs amounts");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<AssessmentVarianceResponse> result = await _mediator.SendAsync(new CreateAssessmentVarianceCommand(
            req.BoeId, req.BoeLineId, req.Type, req.Component,
            req.SystemAmount, req.AssessedAmount, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public Guid BoeLineId { get; set; }
        public VarianceType Type { get; set; }
        public string Component { get; set; } = string.Empty;
        public decimal SystemAmount { get; set; }
        public decimal AssessedAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ListAssessmentVariancesEndpoint : Endpoint<ListAssessmentVariancesEndpoint.Request, IReadOnlyList<AssessmentVarianceResponse>>
{
    private readonly IMediator _mediator;

    public ListAssessmentVariancesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/bills-of-entry/{boeId}/variances");
        Tag(Tags.Documents);
        Summary("List assessment variances for a BoE");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<AssessmentVarianceResponse>> result = await _mediator.QueryAsync(
            new ListAssessmentVariancesByBoeQuery(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class ResolveAssessmentVarianceEndpoint : Endpoint<ResolveAssessmentVarianceEndpoint.Request, AssessmentVarianceResponse>
{
    private readonly IMediator _mediator;

    public ResolveAssessmentVarianceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/assessment-variances/{varianceId}/resolve");
        Tag(Tags.Documents);
        Summary("Resolve an assessment variance");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<AssessmentVarianceResponse> result = await _mediator.SendAsync(
            new ResolveAssessmentVarianceCommand(req.VarianceId, req.Resolution), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VarianceId { get; set; }
        public string Resolution { get; set; } = string.Empty;
    }
}

internal sealed class AcceptAssessmentVarianceEndpoint : Endpoint<AcceptAssessmentVarianceEndpoint.Request, AssessmentVarianceResponse>
{
    private readonly IMediator _mediator;

    public AcceptAssessmentVarianceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/assessment-variances/{varianceId}/accept");
        Tag(Tags.Documents);
        Summary("Accept an assessment variance");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<AssessmentVarianceResponse> result = await _mediator.SendAsync(
            new AcceptAssessmentVarianceCommand(req.VarianceId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VarianceId { get; set; }
    }
}

// ── Port Charge Endpoints (BR-CC-04) ───────────────────────────────

internal sealed class CreatePortChargeEndpoint : Endpoint<CreatePortChargeEndpoint.Request, PortChargeResponse>
{
    private readonly IMediator _mediator;

    public CreatePortChargeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/port-charges");
        Tag(Tags.Documents);
        Summary("Create a port charge (demurrage, detention, port dues, etc.)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PortChargeResponse> result = await _mediator.SendAsync(new CreatePortChargeCommand(
            req.FileId, req.ChargeType, req.Amount, req.Currency, req.ChargedOn, req.Description), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public PortChargeType ChargeType { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateOnly ChargedOn { get; set; }
        public string? Description { get; set; }
    }
}

internal sealed class ListPortChargesByFileEndpoint : Endpoint<ListPortChargesByFileEndpoint.Request, IReadOnlyList<PortChargeResponse>>
{
    private readonly IMediator _mediator;

    public ListPortChargesByFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-files/{fileId}/port-charges");
        Tag(Tags.Documents);
        Summary("List all port charges for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<PortChargeResponse>> result = await _mediator.QueryAsync(
            new ListPortChargesByFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class AcceptPiEndpoint : Endpoint<AcceptPiEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public AcceptPiEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/accept-pi");
        Tag(Tags.ImportFiles);
        Summary("Accept the PI onto the import file (BR-IF-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new AcceptPiCommand(req.FileId, req.PiId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid PiId { get; set; }
    }
}

internal sealed class InstrumentFileEndpoint : Endpoint<InstrumentFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public InstrumentFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/instrument");
        Tag(Tags.ImportFiles);
        Summary("Instrument the file with an LC or TT (BR-IMP-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(
            new InstrumentFileCommand(req.FileId, req.LcId, req.TtId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid? LcId { get; set; }
        public Guid? TtId { get; set; }
    }
}

internal sealed class AdvanceFileEndpoint : Endpoint<AdvanceFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public AdvanceFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/advance");
        Tag(Tags.ImportFiles);
        Summary("Advance the file through the state machine (BR-IF-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new AdvanceFileCommand(
            req.FileId, req.ToStatus, req.ShipmentId, req.LandingDate, req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public ImportFileStatus ToStatus { get; set; }
        public Guid? ShipmentId { get; set; }
        public DateOnly? LandingDate { get; set; }
        public Guid? BoeId { get; set; }
    }
}

internal sealed class AssignCnfAgentEndpoint : Endpoint<AssignCnfAgentEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public AssignCnfAgentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/cnf-agent");
        Tag(Tags.ImportFiles);
        Summary("Assign a C&F agent to the file (BR-IF-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(
            new AssignCnfAgentCommand(req.FileId, req.CnfAgentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid CnfAgentId { get; set; }
    }
}

internal sealed class HoldFileEndpoint : Endpoint<HoldFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public HoldFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/hold");
        Tag(Tags.ImportFiles);
        Summary("Hold the file with a reason");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new HoldFileCommand(req.FileId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ResumeFileEndpoint : Endpoint<ResumeFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public ResumeFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/resume");
        Tag(Tags.ImportFiles);
        Summary("Resume a held file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new ResumeFileCommand(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class MarkDisputedEndpoint : Endpoint<MarkDisputedEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public MarkDisputedEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/dispute");
        Tag(Tags.ImportFiles);
        Summary("Mark the file as disputed");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new MarkDisputedCommand(req.FileId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class CancelFileEndpoint : Endpoint<CancelFileEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public CancelFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/cancel");
        Tag(Tags.ImportFiles);
        Summary("Cancel the file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new CancelFileCommand(req.FileId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class AddFileCostEntryEndpoint : Endpoint<AddFileCostEntryEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public AddFileCostEntryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/cost-entries");
        Tag(Tags.ImportFiles);
        Summary("Add a cost-ledger entry to the file (BR-IMP-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new AddFileCostEntryCommand(
            req.FileId, req.Element, req.AmountFcy, req.AmountBdt, req.Currency, req.SourceDocType,
            req.SourceDocId, req.SourceDocNumber, req.Direction), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string Element { get; set; } = string.Empty;
        public decimal AmountFcy { get; set; }
        public decimal AmountBdt { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string SourceDocType { get; set; } = string.Empty;
        public Guid SourceDocId { get; set; }
        public string SourceDocNumber { get; set; } = string.Empty;
        public CostDirection Direction { get; set; }
    }
}

internal sealed class RegisterFileDocumentEndpoint : Endpoint<RegisterFileDocumentEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public RegisterFileDocumentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/documents");
        Tag(Tags.ImportFiles);
        Summary("Register a document against the file (BR-IMP-08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new RegisterFileDocumentCommand(
            req.FileId, req.Type, req.Name, req.IsMandatory, req.IsPresent), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsMandatory { get; set; }
        public bool IsPresent { get; set; }
    }
}

internal sealed class RegisterContainerEndpoint : Endpoint<RegisterContainerEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public RegisterContainerEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/containers");
        Tag(Tags.ImportFiles);
        Summary("Register a container (ISO 6346 validated, BR-SHP-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(new RegisterContainerCommand(
            req.FileId, req.ContainerNo, req.SizeType, req.IsoCode, req.SealNo), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string ContainerNo { get; set; } = string.Empty;
        public string SizeType { get; set; } = string.Empty;
        public string IsoCode { get; set; } = string.Empty;
        public string? SealNo { get; set; }
    }
}

internal sealed class LandContainerEndpoint : Endpoint<LandContainerEndpoint.Request, ImportFileResponse>
{
    private readonly IMediator _mediator;

    public LandContainerEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-files/{fileId}/containers/{containerId}/land");
        Tag(Tags.ImportFiles);
        Summary("Land a container — starts the demurrage clock (BR-IF-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportFileResponse> result = await _mediator.SendAsync(
            new LandContainerCommand(req.FileId, req.ContainerId, req.LandingDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid ContainerId { get; set; }
        public DateOnly LandingDate { get; set; }
    }
}

internal sealed class CreateProformaInvoiceEndpoint : Endpoint<CreateProformaInvoiceEndpoint.Request, ProformaInvoiceResponse>
{
    private readonly IMediator _mediator;

    public CreateProformaInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/proforma-invoices");
        Tag(Tags.ProformaInvoices);
        Summary("Create a proforma invoice with lines (BR-DOC-01..04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ProformaInvoiceResponse> result = await _mediator.SendAsync(new CreateProformaInvoiceCommand(
            req.FileId, req.PoId, req.PiNumber, req.Currency, req.BeneficiaryName, req.BeneficiaryBank,
            req.BeneficiaryAccount, req.IssuedOn, req.ValidUntil,
            req.Lines.Select(l => new ProFormaLineInput(l.PoLineId, l.Description, l.Quantity, l.Uom, l.UnitPrice)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid PoId { get; set; }
        public string PiNumber { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string BeneficiaryName { get; set; } = string.Empty;
        public string BeneficiaryBank { get; set; } = string.Empty;
        public string BeneficiaryAccount { get; set; } = string.Empty;
        public DateOnly IssuedOn { get; set; }
        public DateOnly ValidUntil { get; set; }
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid? PoLineId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
    }
}

internal sealed class ReconcilePiToPoEndpoint : Endpoint<ReconcilePiToPoEndpoint.Request, ProformaInvoiceResponse>
{
    private readonly IMediator _mediator;

    public ReconcilePiToPoEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/proforma-invoices/{piId}/reconcile-po");
        Tag(Tags.ProformaInvoices);
        Summary("Reconcile a PI line to its PO line within tolerance (BR-DOC-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ProformaInvoiceResponse> result = await _mediator.SendAsync(new ReconcilePiToPoCommand(
            req.PiId, req.PoLineId, req.PoQuantity, req.PoUnitPrice, req.TolerancePct), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PiId { get; set; }
        public Guid PoLineId { get; set; }
        public decimal PoQuantity { get; set; }
        public decimal PoUnitPrice { get; set; }
        public decimal TolerancePct { get; set; }
    }
}

internal sealed class AcceptPiForLcEndpoint : Endpoint<AcceptPiForLcEndpoint.Request, ProformaInvoiceResponse>
{
    private readonly IMediator _mediator;

    public AcceptPiForLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/proforma-invoices/{piId}/accept-for-lc");
        Tag(Tags.ProformaInvoices);
        Summary("Accept the PI for LC (freezes version, checks expiry, BR-DOC-02/03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ProformaInvoiceResponse> result = await _mediator.SendAsync(new AcceptPiForLcCommand(req.PiId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PiId { get; set; }
    }
}

internal sealed class ListProformaInvoicesEndpoint : Endpoint<ListProformaInvoicesEndpoint.Request, IReadOnlyList<ProformaInvoiceResponse>>
{
    private readonly IMediator _mediator;

    public ListProformaInvoicesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/proforma-invoices");
        Tag(Tags.ProformaInvoices);
        Summary("List PIs for a file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ProformaInvoiceResponse>> result = await _mediator.QueryAsync(
            new ListProformaInvoicesQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class CreateCommercialInvoiceEndpoint : Endpoint<CreateCommercialInvoiceEndpoint.Request, CommercialInvoiceResponse>
{
    private readonly IMediator _mediator;

    public CreateCommercialInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/commercial-invoices");
        Tag(Tags.CommercialInvoices);
        Summary("Create a commercial invoice with lines (BR-DOC-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CommercialInvoiceResponse> result = await _mediator.SendAsync(new CreateCommercialInvoiceCommand(
            req.FileId, req.PiId, req.CiNumber, req.Currency, req.TotalFcy, req.IssuedOn,
            req.Lines.Select(l => new CommercialLineInput(l.PiLineId, l.Description, l.Quantity, l.Uom, l.UnitPrice)).ToArray()), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid? PiId { get; set; }
        public string CiNumber { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal TotalFcy { get; set; }
        public DateOnly IssuedOn { get; set; }
        public List<LineInput> Lines { get; set; } = new();
    }

    internal sealed class LineInput
    {
        public Guid? PiLineId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
    }
}

internal sealed class ReconcileCiToPiEndpoint : Endpoint<ReconcileCiToPiEndpoint.Request, CommercialInvoiceResponse>
{
    private readonly IMediator _mediator;

    public ReconcileCiToPiEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/commercial-invoices/{ciId}/reconcile-pi");
        Tag(Tags.CommercialInvoices);
        Summary("Reconcile CI totals to the PI within tolerance (BR-DOC-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CommercialInvoiceResponse> result = await _mediator.SendAsync(
            new ReconcileCiToPiCommand(req.CiId, req.PiTotal, req.TolerancePct), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CiId { get; set; }
        public decimal PiTotal { get; set; }
        public decimal TolerancePct { get; set; }
    }
}

internal sealed class GetCommercialInvoiceEndpoint : Endpoint<GetCommercialInvoiceEndpoint.Request, CommercialInvoiceResponse>
{
    private readonly IMediator _mediator;

    public GetCommercialInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/commercial-invoices/{ciId}");
        Tag(Tags.CommercialInvoices);
        Summary("Get a commercial invoice by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CommercialInvoiceResponse> result = await _mediator.QueryAsync(new GetCommercialInvoiceQuery(req.CiId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CiId { get; set; }
    }
}

internal sealed class CreateShipmentEndpoint : Endpoint<CreateShipmentEndpoint.Request, ShipmentResponse>
{
    private readonly IMediator _mediator;

    public CreateShipmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/shipments");
        Tag(Tags.Shipments);
        Summary("Create a shipment (BR-SHP-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ShipmentResponse> result = await _mediator.SendAsync(new CreateShipmentCommand(
            req.FileId, req.CiId, req.ShipmentNo, req.Mode, req.VesselVoyage, req.Etd, req.Eta), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid? CiId { get; set; }
        public string ShipmentNo { get; set; } = string.Empty;
        public ShipmentMode Mode { get; set; }
        public string VesselVoyage { get; set; } = string.Empty;
        public DateOnly Etd { get; set; }
        public DateOnly Eta { get; set; }
    }
}

internal sealed class GetShipmentEndpoint : Endpoint<GetShipmentEndpoint.Request, ShipmentResponse>
{
    private readonly IMediator _mediator;

    public GetShipmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/shipments/{shipmentId}");
        Tag(Tags.Shipments);
        Summary("Get a shipment by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ShipmentResponse> result = await _mediator.QueryAsync(new GetShipmentQuery(req.ShipmentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
    }
}

internal sealed class RecordEtaChangeEndpoint : Endpoint<RecordEtaChangeEndpoint.Request, ShipmentResponse>
{
    private readonly IMediator _mediator;

    public RecordEtaChangeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/shipments/{shipmentId}/eta");
        Tag(Tags.Shipments);
        Summary("Record an ETA change (BR-SHP-02/03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ShipmentResponse> result = await _mediator.SendAsync(
            new RecordEtaChangeCommand(req.ShipmentId, req.NewEta), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
        public DateOnly NewEta { get; set; }
    }
}

internal sealed class CheckLcBreachRiskEndpoint : Endpoint<CheckLcBreachRiskEndpoint.Request, ShipmentResponse>
{
    private readonly IMediator _mediator;

    public CheckLcBreachRiskEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/shipments/{shipmentId}/lc-breach-check");
        Tag(Tags.Shipments);
        Summary("Check LC latest-shipment-date breach risk at T-7 (BR-SHP-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ShipmentResponse> result = await _mediator.SendAsync(
            new CheckLcBreachRiskCommand(req.ShipmentId, req.LatestShipmentDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
        public DateOnly LatestShipmentDate { get; set; }
    }
}

internal sealed class CreateCnfAgentEndpoint : Endpoint<CreateCnfAgentEndpoint.Request, CnfAgentResponse>
{
    private readonly IMediator _mediator;

    public CreateCnfAgentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cnf-agents");
        Tag(Tags.CnfAgents);
        Summary("Create a C&F agent (BR-CNF-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CnfAgentResponse> result = await _mediator.SendAsync(
            new CreateCnfAgentCommand(req.Name, req.AinNumber, req.Contacts), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Name { get; set; } = string.Empty;
        public string AinNumber { get; set; } = string.Empty;
        public string Contacts { get; set; } = string.Empty;
    }
}

internal sealed class SetCnfRateCardEndpoint : Endpoint<SetCnfRateCardEndpoint.Request, CnfAgentResponse>
{
    private readonly IMediator _mediator;

    public SetCnfRateCardEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/cnf-agents/{agentId}/rate-card");
        Tag(Tags.CnfAgents);
        Summary("Set the C&F agent rate card (BR-CNF-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CnfAgentResponse> result = await _mediator.SendAsync(new SetCnfRateCardCommand(
            req.AgentId, req.PerBoe, req.PerContainer, req.PctOfValue, req.DocumentationCharges), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid AgentId { get; set; }
        public decimal PerBoe { get; set; }
        public decimal PerContainer { get; set; }
        public decimal PctOfValue { get; set; }
        public decimal DocumentationCharges { get; set; }
    }
}

// ── Packing List Endpoints (BR-DOC-06) ──────────────────────────────

internal sealed class CreatePackingListEndpoint : Endpoint<CreatePackingListEndpoint.Request, PackingListResponse>
{
    private readonly IMediator _mediator;

    public CreatePackingListEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/packing-lists");
        Tag(Tags.Documents);
        Summary("Create a packing list linked to a commercial invoice (BR-DOC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PackingListResponse> result = await _mediator.SendAsync(new CreatePackingListCommand(
            req.FileId, req.CiId, req.PlNumber, req.Cartons, req.NetWeightKg,
            req.GrossWeightKg, req.VolumeCbm, req.Lines), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid CiId { get; set; }
        public string PlNumber { get; set; } = string.Empty;
        public int Cartons { get; set; }
        public decimal NetWeightKg { get; set; }
        public decimal GrossWeightKg { get; set; }
        public decimal VolumeCbm { get; set; }
        public List<PackingListLineInput> Lines { get; set; } = new();
    }
}

internal sealed class ValidatePackingListEndpoint : Endpoint<ValidatePackingListEndpoint.Request, PackingListResponse>
{
    private readonly IMediator _mediator;

    public ValidatePackingListEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/packing-lists/{plId}/validate");
        Tag(Tags.Documents);
        Summary("Validate PL quantities total CI quantities within tolerance (BR-DOC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<PackingListResponse> result = await _mediator.SendAsync(
            new ValidatePackingListCommand(req.PlId, req.CiQuantity, req.TolerancePct), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PlId { get; set; }
        public decimal CiQuantity { get; set; }
        public decimal TolerancePct { get; set; } = 0.02m;
    }
}

// ── Import Permit Endpoints (BR-PM-01/02) ───────────────────────────

internal sealed class CreateImportPermitEndpoint : Endpoint<CreateImportPermitEndpoint.Request, ImportPermitResponse>
{
    private readonly IMediator _mediator;

    public CreateImportPermitEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-permits");
        Tag(Tags.Documents);
        Summary("Create an import permit with ceiling tracking (BR-PM-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPermitResponse> result = await _mediator.SendAsync(new CreateImportPermitCommand(
            req.PermitNo, req.Category, req.CeilingQty, req.CeilingValue,
            req.IssuedOn, req.ExpiresOn, req.IssuedBy), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string PermitNo { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal CeilingQty { get; set; }
        public decimal CeilingValue { get; set; }
        public DateOnly IssuedOn { get; set; }
        public DateOnly ExpiresOn { get; set; }
        public string IssuedBy { get; set; } = string.Empty;
    }
}

internal sealed class DrawPermitEndpoint : Endpoint<DrawPermitEndpoint.Request, ImportPermitResponse>
{
    private readonly IMediator _mediator;

    public DrawPermitEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/import-permits/{permitId}/draw");
        Tag(Tags.Documents);
        Summary("Draw on permit ceiling for a consignment (BR-PM-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ImportPermitResponse> result = await _mediator.SendAsync(
            new DrawPermitCommand(req.PermitId, req.FileId, req.Qty, req.Value), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid PermitId { get; set; }
        public Guid FileId { get; set; }
        public decimal Qty { get; set; }
        public decimal Value { get; set; }
    }
}

// ── Insurance Policy Endpoints (BR-INS-01) ──────────────────────────

internal sealed class CreateInsurancePolicyEndpoint : Endpoint<CreateInsurancePolicyEndpoint.Request, InsurancePolicyResponse>
{
    private readonly IMediator _mediator;

    public CreateInsurancePolicyEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/insurance-policies");
        Tag(Tags.Documents);
        Summary("Create a marine/air insurance policy for an import file (BR-INS-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<InsurancePolicyResponse> result = await _mediator.SendAsync(new CreateInsurancePolicyCommand(
            req.FileId, req.PolicyNo, req.Insurer, req.CoverNoteRef,
            req.InsuredValueFcy, req.PremiumFcy, req.Currency, req.CoverStart), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string PolicyNo { get; set; } = string.Empty;
        public string Insurer { get; set; } = string.Empty;
        public string CoverNoteRef { get; set; } = string.Empty;
        public decimal InsuredValueFcy { get; set; }
        public decimal PremiumFcy { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateOnly CoverStart { get; set; }
    }
}

// ── Transport Document Endpoints (BR-BL-01..03) ────────────────────

internal sealed class CreateTransportDocumentEndpoint : Endpoint<CreateTransportDocumentEndpoint.Request, TransportDocumentResponse>
{
    private readonly IMediator _mediator;

    public CreateTransportDocumentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/transport-documents");
        Tag(Tags.Documents);
        Summary("Create a transport document (MBL/HBL/MAWB/HAWB) for a shipment (BR-BL-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TransportDocumentResponse> result = await _mediator.SendAsync(new CreateTransportDocumentCommand(
            req.ShipmentId, req.FileId, req.Type, req.DocumentNumber, req.IssueDate,
            req.OnBoardDate, req.FreightTerms, req.Consignee, req.NotifyParty,
            req.OriginalCount, req.SurrenderStatus), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
        public Guid FileId { get; set; }
        public TransportDocumentType Type { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateOnly IssueDate { get; set; }
        public DateOnly? OnBoardDate { get; set; }
        public string FreightTerms { get; set; } = string.Empty;
        public string Consignee { get; set; } = string.Empty;
        public string NotifyParty { get; set; } = string.Empty;
        public int OriginalCount { get; set; }
        public SurrenderStatus SurrenderStatus { get; set; }
    }
}

internal sealed class TransferTransportDocumentEndpoint : Endpoint<TransferTransportDocumentEndpoint.Request, TransportDocumentResponse>
{
    private readonly IMediator _mediator;

    public TransferTransportDocumentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/transport-documents/{documentId}/transfer");
        Tag(Tags.Documents);
        Summary("Transfer transport document custody (bank endorsement, BR-BL-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TransportDocumentResponse> result = await _mediator.SendAsync(
            new TransferTransportDocumentCommand(req.DocumentId, req.NewHolder), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid DocumentId { get; set; }
        public CustodyHolder NewHolder { get; set; }
    }
}

internal sealed class ListTransportDocumentsByShipmentEndpoint : Endpoint<ListTransportDocumentsByShipmentEndpoint.Request, IReadOnlyList<TransportDocumentResponse>>
{
    private readonly IMediator _mediator;

    public ListTransportDocumentsByShipmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/shipments/{shipmentId}/transport-documents");
        Tag(Tags.Documents);
        Summary("List transport documents for a shipment");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<TransportDocumentResponse>> result = await _mediator.QueryAsync(
            new ListTransportDocumentsByShipmentQuery(req.ShipmentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
    }
}

// ── Freight Cost Endpoints (BR-FR-01/02) ───────────────────────────

internal sealed class CreateFreightCostEndpoint : Endpoint<CreateFreightCostEndpoint.Request, FreightCostResponse>
{
    private readonly IMediator _mediator;

    public CreateFreightCostEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/freight-costs");
        Tag(Tags.Shipments);
        Summary("Create a freight cost entry (BR-FR-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<FreightCostResponse> result = await _mediator.SendAsync(new CreateFreightCostCommand(
            req.ShipmentId, req.FileId, req.CostType, req.Description,
            req.Amount, req.Currency, req.SurchargeType), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ShipmentId { get; set; }
        public Guid FileId { get; set; }
        public FreightCostType CostType { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? SurchargeType { get; set; }
    }
}

internal sealed class CommitFreightCostToActualEndpoint : Endpoint<CommitFreightCostToActualEndpoint.Request, FreightCostResponse>
{
    private readonly IMediator _mediator;

    public CommitFreightCostToActualEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/freight-costs/{freightCostId}/commit");
        Tag(Tags.Shipments);
        Summary("Commit estimated freight cost to actual with invoice (BR-FR-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<FreightCostResponse> result = await _mediator.SendAsync(
            new CommitFreightCostToActualCommand(req.FreightCostId, req.InvoiceNo, req.InvoiceDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FreightCostId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public DateOnly InvoiceDate { get; set; }
    }
}

internal sealed class ListFreightCostsByFileEndpoint : Endpoint<ListFreightCostsByFileEndpoint.Request, IReadOnlyList<FreightCostResponse>>
{
    private readonly IMediator _mediator;

    public ListFreightCostsByFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/import-files/{fileId}/freight-costs");
        Tag(Tags.Shipments);
        Summary("List all freight costs for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<FreightCostResponse>> result = await _mediator.QueryAsync(
            new ListFreightCostsByFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}
// ── Certificate of Origin (BR-COO-01..06) ──────────────────────────

internal sealed class CreateCertificateOfOriginEndpoint : Endpoint<CreateCertificateOfOriginEndpoint.Request, CertificateOfOriginResponse>
{
    private readonly IMediator _mediator;

    public CreateCertificateOfOriginEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/certificates-of-origin");
        Tag(Tags.CertificateOfOrigin);
        Summary("Create a Certificate of Origin (BR-COO-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CertificateOfOriginResponse> result = await _mediator.SendAsync(
            new CreateCertificateOfOriginCommand(req.FileId, req.CiId, req.Type, req.OriginCountry,
                req.DocumentNo, req.IssuerName, req.IssuedOn, req.ExpiryDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public Guid? CiId { get; set; }
        public CertificateOfOriginType Type { get; set; }
        public string OriginCountry { get; set; } = string.Empty;
        public string DocumentNo { get; set; } = string.Empty;
        public string? IssuerName { get; set; }
        public DateOnly IssuedOn { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}

internal sealed class CheckCooOriginMismatchEndpoint : Endpoint<CheckCooOriginMismatchEndpoint.Request, CertificateOfOriginResponse>
{
    private readonly IMediator _mediator;

    public CheckCooOriginMismatchEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/certificates-of-origin/check-mismatch");
        Tag(Tags.CertificateOfOrigin);
        Summary("Check COO origin mismatch with CI (BR-COO-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CertificateOfOriginResponse> result = await _mediator.SendAsync(
            new CheckCooOriginMismatchCommand(req.FileId, req.CiOriginCountry), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
        public string CiOriginCountry { get; set; } = string.Empty;
    }
}

internal sealed class CreateCooIssuerRegistryEndpoint : Endpoint<CreateCooIssuerRegistryEndpoint.Request, CooIssuerRegistryResponse>
{
    private readonly IMediator _mediator;

    public CreateCooIssuerRegistryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/coo-issuer-registries");
        Tag(Tags.CertificateOfOrigin);
        Summary("Create a COO issuer registry entry (BR-COO-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<CooIssuerRegistryResponse> result = await _mediator.SendAsync(
            new CreateCooIssuerRegistryCommand(req.Country, req.IssuerName, req.LicenseNo,
                req.ValidFrom, req.ValidTo), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Country { get; set; } = string.Empty;
        public string IssuerName { get; set; } = string.Empty;
        public string? LicenseNo { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }
}
