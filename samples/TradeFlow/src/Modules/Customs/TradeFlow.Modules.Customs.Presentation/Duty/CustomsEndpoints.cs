using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Customs.Application.Duty.Commands;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Application.Duty.Queries;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Presentation.Duty;

internal sealed class CreateHsCodeEndpoint : Endpoint<CreateHsCodeEndpoint.Request, HsCodeResponse>
{
    private readonly IMediator _mediator;

    public CreateHsCodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/hs-codes");
        Tag(Tags.HsCodes);
        Summary("Create an effective-dated HS code tariff line (BR-HS-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<HsCodeResponse> result = await _mediator.SendAsync(new CreateHsCodeCommand(
            req.Code, req.Description, req.EffectiveFrom, req.EffectiveTo), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
    }
}

internal sealed class CreateDutyRateEndpoint : Endpoint<CreateDutyRateEndpoint.Request, DutyRateResponse>
{
    private readonly IMediator _mediator;

    public CreateDutyRateEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/duty-rates");
        Tag(Tags.DutyRates);
        Summary("Create a pending duty-rate row (maker; approved by checker, BR-DS-01/02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<DutyRateResponse> result = await _mediator.SendAsync(new CreateDutyRateCommand(
            req.HsCode, req.Component, req.Rate, req.EffectiveFrom, req.EffectiveTo, req.Source,
            req.SpecificRate, req.Uom, req.RefDoc), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string HsCode { get; set; } = string.Empty;
        public DutyComponent Component { get; set; }
        public decimal Rate { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public DutyRateSource Source { get; set; }
        public decimal? SpecificRate { get; set; }
        public string? Uom { get; set; }
        public string? RefDoc { get; set; }
    }
}

internal sealed class ApproveDutyRateEndpoint : Endpoint<ApproveDutyRateEndpoint.Request, DutyRateResponse>
{
    private readonly IMediator _mediator;

    public ApproveDutyRateEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/duty-rates/{rateId}/approve");
        Tag(Tags.DutyRates);
        Summary("Approve a duty-rate change (checker, BR-DS-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<DutyRateResponse> result = await _mediator.SendAsync(new ApproveDutyRateCommand(req.RateId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RateId { get; set; }
    }
}

internal sealed class RejectDutyRateEndpoint : Endpoint<RejectDutyRateEndpoint.Request, object>
{
    private readonly IMediator _mediator;

    public RejectDutyRateEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/duty-rates/{rateId}/reject");
        Tag(Tags.DutyRates);
        Summary("Reject a duty-rate change (checker, BR-DS-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RejectDutyRateCommand(req.RateId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid RateId { get; set; }
        public string? Reason { get; set; }
    }
}

internal sealed class CreateSroBenefitEndpoint : Endpoint<CreateSroBenefitEndpoint.Request, SroBenefitResponse>
{
    private readonly IMediator _mediator;

    public CreateSroBenefitEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/sro-benefits");
        Tag(Tags.SroBenefits);
        Summary("Register an SRO benefit (exempt / rate override / cap; BR-DS-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<SroBenefitResponse> result = await _mediator.SendAsync(new CreateSroBenefitCommand(
            req.Name, req.HsCodePrefix, req.Type, req.EffectiveFrom, req.EffectiveTo,
            req.OverrideRate, req.CapPercent, req.Conditions), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Name { get; set; } = string.Empty;
        public string HsCodePrefix { get; set; } = string.Empty;
        public SroBenefitType Type { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public decimal? OverrideRate { get; set; }
        public decimal? CapPercent { get; set; }
        public string Conditions { get; set; } = string.Empty;
    }
}

internal sealed class EstimateDutyEndpoint : Endpoint<EstimateDutyEndpoint.Request, DutyEstimateResponse>
{
    private readonly IMediator _mediator;

    public EstimateDutyEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/duty/estimate");
        Tag(Tags.Duty);
        Summary("What-if duty estimate for an HS line at a date (BR-AI-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<DutyEstimateResponse> result = await _mediator.SendAsync(new EstimateDutyCommand(
            req.HsCode, req.Quantity, req.UnitPriceFcy, req.FreightShareFcy, req.InsuranceShareFcy,
            req.CustomsExchangeRate, req.AssessmentDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string HsCode { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPriceFcy { get; set; }
        public decimal FreightShareFcy { get; set; }
        public decimal InsuranceShareFcy { get; set; }
        public decimal CustomsExchangeRate { get; set; }
        public DateOnly AssessmentDate { get; set; }
    }
}

internal sealed class CreateBoeEndpoint : Endpoint<CreateBoeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public CreateBoeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes");
        Tag(Tags.BillsOfEntry);
        Summary("Mirror a Bill of Entry with lines (BR-CUS-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var lines = req.Lines.Select(l => new BoeLineInput(l.CiLineId, l.HsCode, l.Description, l.Quantity, l.Uom,
            l.DeclaredAvFcy, l.CustomsExchangeRate, l.LandingChargePct, l.TariffValueBdt)).ToList();

        Result<BoeResponse> result = await _mediator.SendAsync(new CreateBoeCommand(
            req.FileId, req.BoeNo, req.BoeDate, req.OfficeCode, req.DeclarantAin, lines), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/boes/{result.Value.Id}", ct);
    }

    internal sealed class Request
    {
        public Guid? FileId { get; set; }
        public string BoeNo { get; set; } = string.Empty;
        public DateOnly BoeDate { get; set; }
        public string OfficeCode { get; set; } = string.Empty;
        public string DeclarantAin { get; set; } = string.Empty;
        public List<Line> Lines { get; set; } = new();
    }

    internal sealed class Line
    {
        public Guid? CiLineId { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Uom { get; set; } = string.Empty;
        public decimal DeclaredAvFcy { get; set; }
        public decimal CustomsExchangeRate { get; set; }
        public decimal LandingChargePct { get; set; }
        public decimal? TariffValueBdt { get; set; }
    }
}

internal sealed class GetBoeEndpoint : Endpoint<GetBoeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public GetBoeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/boes/{boeId}");
        Tag(Tags.BillsOfEntry);
        Summary("Get a BoE with lines, challans, disputes and milestones");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.QueryAsync(new GetBoeQuery(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class AssessBoeEndpoint : Endpoint<AssessBoeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public AssessBoeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes/{boeId}/assess");
        Tag(Tags.BillsOfEntry);
        Summary("Assess a BoE — compute the duty cascade, record rate lineage (BR-DS-04) and post AIT/AT ledger additions (BR-CUS-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var assessed = req.AssessedLines?
            .Select(a => new AssessedLineInput(a.LineId,
                a.AssessedDutyLines.Select(d => new AssessedDutyLineResponse(d.Component, d.Amount)).ToList(),
                a.AssessedTtiBdt))
            .ToList();

        Result<BoeResponse> result = await _mediator.SendAsync(new AssessBoeCommand(
            req.BoeId, req.CompanyId, req.TolerancePct, assessed), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public Guid CompanyId { get; set; }
        public decimal TolerancePct { get; set; }
        public List<AssessedLine>? AssessedLines { get; set; }
    }

    internal sealed class AssessedLine
    {
        public Guid LineId { get; set; }
        public decimal AssessedTtiBdt { get; set; }
        public List<AssessedDutyLineResponse> AssessedDutyLines { get; set; } = new();
    }
}

internal sealed class RegisterChallanEndpoint : Endpoint<RegisterChallanEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public RegisterChallanEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes/{boeId}/challans");
        Tag(Tags.BillsOfEntry);
        Summary("Register a challan against assessed TTI (BR-CUS-06/08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.SendAsync(new RegisterChallanCommand(
            req.BoeId, req.ChallanNo, req.Amount, req.PaidAtUtc, req.EvidenceRef), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public string ChallanNo { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime PaidAtUtc { get; set; }
        public string? EvidenceRef { get; set; }
    }
}

internal sealed class ExamineBoeEndpoint : Endpoint<ExamineBoeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public ExamineBoeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes/{boeId}/examine");
        Tag(Tags.BillsOfEntry);
        Summary("Record examination outcome and lane G/Y/R (BR-CUS-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.SendAsync(new ExamineBoeCommand(req.BoeId, req.Lane), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public ExaminationLane Lane { get; set; }
    }
}

internal sealed class ReleaseBoeEndpoint : Endpoint<ReleaseBoeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public ReleaseBoeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes/{boeId}/release");
        Tag(Tags.BillsOfEntry);
        Summary("Release a BoE — blocked until assessment is fully paid (BR-CUS-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.SendAsync(new ReleaseBoeCommand(req.BoeId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
    }
}

internal sealed class GetAitAtLedgerEndpoint : Endpoint<GetAitAtLedgerEndpoint.Request, AitAtLedgerResponse>
{
    private readonly IMediator _mediator;

    public GetAitAtLedgerEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/ait-at-ledger/{companyId}/{fiscalYear}");
        Tag(Tags.AitAtLedger);
        Summary("AIT/AT advance-tax sub-ledger for a company and FY (BR-CUS-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<AitAtLedgerResponse> result = await _mediator.QueryAsync(new GetAitAtLedgerQuery(req.CompanyId, req.FiscalYear), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CompanyId { get; set; }
        public int FiscalYear { get; set; }
    }
}

internal sealed class RecordAitAtAdjustmentEndpoint : Endpoint<RecordAitAtAdjustmentEndpoint.Request, AitAtLedgerEntryResponse>
{
    private readonly IMediator _mediator;

    public RecordAitAtAdjustmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/ait-at-ledger/adjustments");
        Tag(Tags.AitAtLedger);
        Summary("Counterpost an AIT/AT adjustment per return period (BR-CUS-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<AitAtLedgerEntryResponse> result = await _mediator.SendAsync(new RecordAitAtAdjustmentCommand(
            req.CompanyId, req.FiscalYear, req.Component, req.Amount, req.ReturnPeriod,
            req.Narrative, req.BookedOn), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CompanyId { get; set; }
        public int FiscalYear { get; set; }
        public DutyComponent Component { get; set; }
        public decimal Amount { get; set; }
        public string ReturnPeriod { get; set; } = string.Empty;
        public string? Narrative { get; set; }
        public DateOnly BookedOn { get; set; }
    }
}

internal sealed class AccrueDemurrageEndpoint : Endpoint<AccrueDemurrageEndpoint.Request, DemurrageResponse>
{
    private readonly IMediator _mediator;

    public AccrueDemurrageEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/demurrage/accrue");
        Tag(Tags.Demurrage);
        Summary("Accrue demurrage daily after free time (BR-CUS-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<DemurrageResponse> result = await _mediator.SendAsync(new AccrueDemurrageCommand(
            req.FileId, req.ContainerRef, req.PortCode, req.LandingDate, req.FreeDays,
            req.DailyRateBdt, req.AsOfDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid? FileId { get; set; }
        public string ContainerRef { get; set; } = string.Empty;
        public string PortCode { get; set; } = string.Empty;
        public DateOnly LandingDate { get; set; }
        public int FreeDays { get; set; }
        public decimal DailyRateBdt { get; set; }
        public DateOnly AsOfDate { get; set; }
    }
}

internal sealed class GetDemurrageForFileEndpoint : Endpoint<GetDemurrageForFileEndpoint.Request, IReadOnlyList<DemurrageResponse>>
{
    private readonly IMediator _mediator;

    public GetDemurrageForFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/demurrage/file/{fileId}");
        Tag(Tags.Demurrage);
        Summary("Demurrage accruals for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<DemurrageResponse>> result = await _mediator.QueryAsync(new GetDemurrageForFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class GetBoeByFileEndpoint : Endpoint<GetBoeByFileEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public GetBoeByFileEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/files/{fileId}/boe");
        Tag(Tags.BillsOfEntry);
        Summary("Get the Bill of Entry for an import file");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.QueryAsync(new GetBoeByFileQuery(req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid FileId { get; set; }
    }
}

internal sealed class ListBoesEndpoint : Endpoint<ListBoesEndpoint.Request, IReadOnlyList<BoeResponse>>
{
    private readonly IMediator _mediator;

    public ListBoesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/boes");
        Tag(Tags.BillsOfEntry);
        Summary("List all Bills of Entry for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<BoeResponse>> result = await _mediator.QueryAsync(new ListBoesQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class SearchHsCodesEndpoint : Endpoint<SearchHsCodesEndpoint.Request, IReadOnlyList<HsCodeResponse>>
{
    private readonly IMediator _mediator;

    public SearchHsCodesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/hs-codes/search");
        Tag(Tags.HsCodes);
        Summary("Search HS codes by chapter prefix");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<HsCodeResponse>> result = await _mediator.QueryAsync(
            new SearchHsCodesQuery(req.ChapterPrefix), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string ChapterPrefix { get; set; } = string.Empty;
    }
}

internal sealed class ListDutyRatesByHsCodeEndpoint : Endpoint<ListDutyRatesByHsCodeEndpoint.Request, IReadOnlyList<DutyRateResponse>>
{
    private readonly IMediator _mediator;

    public ListDutyRatesByHsCodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/hs-codes/{hsCode}/duty-rates");
        Tag(Tags.DutyRates);
        Summary("List all duty rates for an HS code");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<DutyRateResponse>> result = await _mediator.QueryAsync(
            new ListDutyRatesByHsCodeQuery(req.HsCode), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string HsCode { get; set; } = string.Empty;
    }
}

internal sealed class ListSroBenefitsEndpoint : Endpoint<ListSroBenefitsEndpoint.Request, IReadOnlyList<SroBenefitResponse>>
{
    private readonly IMediator _mediator;

    public ListSroBenefitsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/sro-benefits");
        Tag(Tags.SroBenefits);
        Summary("List all SRO benefits");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<SroBenefitResponse>> result = await _mediator.QueryAsync(new ListSroBenefitsQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class ResolveSroBenefitsEndpoint : Endpoint<ResolveSroBenefitsEndpoint.Request, SroSourceResponse>
{
    private readonly IMediator _mediator;

    public ResolveSroBenefitsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/duty/sro-source");
        Tag(Tags.SroBenefits);
        Summary("Source the duty structure for an HS code: base rates + resolved SRO benefits with effective rates (BR-DS-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<SroSourceResponse> result = await _mediator.QueryAsync(
            new ResolveSroBenefitsQuery(req.HsCode, req.AsOfDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string HsCode { get; set; } = string.Empty;
        public DateOnly AsOfDate { get; set; }
    }
}

internal sealed class BulkDutyLookupEndpoint : Endpoint<BulkDutyLookupEndpoint.Request, IReadOnlyList<BulkDutyLookupEntryResponse>>
{
    private readonly IMediator _mediator;

    public BulkDutyLookupEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/duty/bulk-lookup");
        Tag(Tags.Duty);
        Summary("Bulk tax lookup across many HS codes: effective rates + applicable SRO benefits (max 50 per call)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await _mediator.QueryAsync(
            new BulkDutyLookupQuery(req.HsCodes, req.AsOfDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public IReadOnlyList<string> HsCodes { get; set; } = [];
        public DateOnly AsOfDate { get; set; }
    }
}

internal sealed class GetDutyAnalysisEndpoint : Endpoint<GetDutyAnalysisEndpoint.Request, DutyAnalysisResponse>
{
    private readonly IMediator _mediator;

    public GetDutyAnalysisEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/duty/analysis");
        Tag(Tags.Duty);
        Summary("Duty Analysis report: component mix by HS, computed-vs-assessed variance, effective duty %, SRO savings (doc 08 Report 3)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<DutyAnalysisResponse> result = await _mediator.QueryAsync(
            new GetDutyAnalysisQuery(req.From, req.To), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
    }
}

internal sealed class ResolveDisputeEndpoint : Endpoint<ResolveDisputeEndpoint.Request, BoeResponse>
{
    private readonly IMediator _mediator;

    public ResolveDisputeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/boes/{boeId}/disputes/{disputeId}/resolve");
        Tag(Tags.BillsOfEntry);
        Summary("Resolve a BoE assessment dispute");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BoeResponse> result = await _mediator.SendAsync(
            new ResolveDisputeCommand(req.BoeId, req.DisputeId, req.ResolutionType, req.Notes), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BoeId { get; set; }
        public Guid DisputeId { get; set; }
        public DisputeResolutionType ResolutionType { get; set; }
        public string? Notes { get; set; }
    }
}

// ── Item-HS Mapping Endpoints (BR-HS-02..03) ───────────────────────

internal sealed class CreateItemHsMappingEndpoint : Endpoint<CreateItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public CreateItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/item-hs-mappings");
        Tag(Tags.HsCodes);
        Summary("Create an item-to-HS mapping (BR-HS-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.SendAsync(
            new CreateItemHsMappingCommand(req.ItemId, req.HsCode, req.Confidence,
                req.Notes, req.IsConsignmentOverride, req.FileId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ItemId { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public string? Notes { get; set; }
        public bool IsConsignmentOverride { get; set; }
        public Guid? FileId { get; set; }
    }
}

internal sealed class GetItemHsMappingEndpoint : Endpoint<GetItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public GetItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/item-hs-mappings/{mappingId}");
        Tag(Tags.HsCodes);
        Summary("Get an item-HS mapping by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.QueryAsync(
            new GetItemHsMappingQuery(req.MappingId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid MappingId { get; set; }
    }
}

internal sealed class GetItemHsMappingByItemEndpoint : Endpoint<GetItemHsMappingByItemEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public GetItemHsMappingByItemEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/items/{itemId}/hs-mapping");
        Tag(Tags.HsCodes);
        Summary("Get the approved HS mapping for an item");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.QueryAsync(
            new GetItemHsMappingByItemQuery(req.ItemId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid ItemId { get; set; }
    }
}

internal sealed class ListItemHsMappingsEndpoint : Endpoint<ListItemHsMappingsEndpoint.Request, IReadOnlyList<ItemHsMappingResponse>>
{
    private readonly IMediator _mediator;

    public ListItemHsMappingsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/item-hs-mappings");
        Tag(Tags.HsCodes);
        Summary("List all item-HS mappings for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ItemHsMappingResponse>> result = await _mediator.QueryAsync(
            new ListItemHsMappingsQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class ListItemHsMappingsByHsCodeEndpoint : Endpoint<ListItemHsMappingsByHsCodeEndpoint.Request, IReadOnlyList<ItemHsMappingResponse>>
{
    private readonly IMediator _mediator;

    public ListItemHsMappingsByHsCodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/hs-codes/{hsCode}/item-mappings");
        Tag(Tags.HsCodes);
        Summary("List all items mapped to an HS code");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<ItemHsMappingResponse>> result = await _mediator.QueryAsync(
            new ListItemHsMappingsByHsCodeQuery(req.HsCode), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string HsCode { get; set; } = string.Empty;
    }
}

internal sealed class UpdateItemHsMappingEndpoint : Endpoint<UpdateItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public UpdateItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/item-hs-mappings/{mappingId}");
        Tag(Tags.HsCodes);
        Summary("Update an item-HS mapping (BR-HS-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.SendAsync(
            new UpdateItemHsMappingCommand(req.MappingId, req.HsCode, req.Confidence, req.Notes), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid MappingId { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public string? Notes { get; set; }
    }
}

internal sealed class SubmitItemHsMappingEndpoint : Endpoint<SubmitItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public SubmitItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/item-hs-mappings/{mappingId}/submit");
        Tag(Tags.HsCodes);
        Summary("Submit an item-HS mapping for approval (maker-checker)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.SendAsync(
            new SubmitItemHsMappingCommand(req.MappingId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid MappingId { get; set; }
    }
}

internal sealed class ApproveItemHsMappingEndpoint : Endpoint<ApproveItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public ApproveItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/item-hs-mappings/{mappingId}/approve");
        Tag(Tags.HsCodes);
        Summary("Approve an item-HS mapping (checker: Finance Head)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.SendAsync(
            new ApproveItemHsMappingCommand(req.MappingId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid MappingId { get; set; }
    }
}

internal sealed class RejectItemHsMappingEndpoint : Endpoint<RejectItemHsMappingEndpoint.Request, ItemHsMappingResponse>
{
    private readonly IMediator _mediator;

    public RejectItemHsMappingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/item-hs-mappings/{mappingId}/reject");
        Tag(Tags.HsCodes);
        Summary("Reject an item-HS mapping (checker)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<ItemHsMappingResponse> result = await _mediator.SendAsync(
            new RejectItemHsMappingCommand(req.MappingId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid MappingId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}