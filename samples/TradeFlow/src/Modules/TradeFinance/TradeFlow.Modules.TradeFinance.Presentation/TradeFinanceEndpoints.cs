using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.TradeFinance.Application.Commands;
using TradeFlow.Modules.TradeFinance.Application.Dtos;
using TradeFlow.Modules.TradeFinance.Application.Queries;
using TradeFlow.Modules.TradeFinance.Domain.Entities;
using TradeFlow.Modules.TradeFinance.Domain.Rules;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.TradeFinance.Presentation;

internal sealed class CreateLcEndpoint : Endpoint<CreateLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public CreateLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs");
        Tag(Tags.LettersOfCredit);
        Summary("Create a draft Letter of Credit (BR-LC-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new CreateLcCommand(
            req.FileId, req.PoId, req.LcNumber, req.Type, req.Currency, req.Amount, req.TolerancePct,
            req.ApplicantCompanyId, req.BeneficiaryVendorId, req.BeneficiaryName, req.IssuingBankId,
            req.LatestShipmentDate, req.ExpiryDate, req.Incoterm, req.PortOfLoading, req.PortOfDischarge,
            req.PartialShipmentAllowed, req.TransshipmentAllowed, req.MarginPct, req.BookingFxRate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid? FileId { get; set; }
        public Guid? PoId { get; set; }
        public string LcNumber { get; set; } = string.Empty;
        public LcType Type { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal TolerancePct { get; set; }
        public Guid ApplicantCompanyId { get; set; }
        public Guid BeneficiaryVendorId { get; set; }
        public string BeneficiaryName { get; set; } = string.Empty;
        public Guid IssuingBankId { get; set; }
        public DateOnly LatestShipmentDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public string Incoterm { get; set; } = string.Empty;
        public string PortOfLoading { get; set; } = string.Empty;
        public string PortOfDischarge { get; set; } = string.Empty;
        public bool PartialShipmentAllowed { get; set; }
        public bool TransshipmentAllowed { get; set; }
        public decimal MarginPct { get; set; }
        public decimal BookingFxRate { get; set; }
    }
}

internal sealed class SubmitLcApplicationEndpoint : Endpoint<SubmitLcApplicationEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public SubmitLcApplicationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/submit");
        Tag(Tags.LettersOfCredit);
        Summary("Submit LC application — prerequisite gate + term consistency (BR-LC-01/03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var prerequisites = new LcPrerequisiteInput(
            req.Prerequisites.HasAcceptedPi, req.Prerequisites.HasInsuranceCoverNote,
            req.Prerequisites.HasInsuranceMoneyReceipt, req.Prerequisites.HasValidIrc,
            req.Prerequisites.IrcHasCeiling, req.Prerequisites.IsLcaComplete,
            req.Prerequisites.IsHsClassified, req.Prerequisites.IsPermitCheckPassed,
            req.Prerequisites.IsOnCfrOrFobTerms);

        var terms = new LcTermConsistencyInput(
            req.Terms.LcCurrency, req.Terms.PoCurrency, req.Terms.LcAmount, req.Terms.PoAmount,
            req.Terms.TolerancePct, req.Terms.LcLatestShipmentDate, req.Terms.PoLatestShipmentDate,
            req.Terms.LcExpiryDate, req.Terms.LcPortOfLoadingName, req.Terms.LcPortOfDischargeName,
            req.Terms.PoPortOfLoadingName, req.Terms.PoPortOfDischargeName,
            req.Terms.LcPartialShipmentAllowed, req.Terms.PoPartialShipmentAllowed,
            req.Terms.LcTransshipmentAllowed, req.Terms.PoTransshipmentAllowed,
            req.Terms.LcIncoterm, req.Terms.PoIncoterm);

        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new SubmitLcApplicationCommand(req.LcId, prerequisites, terms), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public PrerequisitesInput Prerequisites { get; set; } = new();
        public TermsInput Terms { get; set; } = new();
    }

    internal sealed class PrerequisitesInput
    {
        public bool HasAcceptedPi { get; set; }
        public bool HasInsuranceCoverNote { get; set; }
        public bool HasInsuranceMoneyReceipt { get; set; }
        public bool HasValidIrc { get; set; }
        public bool IrcHasCeiling { get; set; }
        public bool IsLcaComplete { get; set; }
        public bool IsHsClassified { get; set; }
        public bool IsPermitCheckPassed { get; set; }
        public bool IsOnCfrOrFobTerms { get; set; }
    }

    internal sealed class TermsInput
    {
        public string LcCurrency { get; set; } = string.Empty;
        public string PoCurrency { get; set; } = string.Empty;
        public decimal LcAmount { get; set; }
        public decimal PoAmount { get; set; }
        public decimal TolerancePct { get; set; }
        public DateOnly? LcLatestShipmentDate { get; set; }
        public DateOnly? PoLatestShipmentDate { get; set; }
        public DateOnly LcExpiryDate { get; set; }
        public string LcPortOfLoadingName { get; set; } = string.Empty;
        public string LcPortOfDischargeName { get; set; } = string.Empty;
        public string PoPortOfLoadingName { get; set; } = string.Empty;
        public string PoPortOfDischargeName { get; set; } = string.Empty;
        public bool LcPartialShipmentAllowed { get; set; }
        public bool PoPartialShipmentAllowed { get; set; }
        public bool LcTransshipmentAllowed { get; set; }
        public bool PoTransshipmentAllowed { get; set; }
        public string LcIncoterm { get; set; } = string.Empty;
        public string PoIncoterm { get; set; } = string.Empty;
    }
}

internal sealed class ApproveLcApplicationEndpoint : Endpoint<ApproveLcApplicationEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public ApproveLcApplicationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/approve-application");
        Tag(Tags.LettersOfCredit);
        Summary("Approve the LC application (CFO)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new ApproveLcApplicationCommand(req.LcId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
    }
}

internal sealed class IssueLcEndpoint : Endpoint<IssueLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public IssueLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/issue");
        Tag(Tags.LettersOfCredit);
        Summary("Issue the LC — margin block + facility reservation (BR-LC-04/05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new IssueLcCommand(req.LcId, req.FacilityAvailable, req.FacilityOverride), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public decimal FacilityAvailable { get; set; }
        public bool FacilityOverride { get; set; }
    }
}

internal sealed class RequestLcAmendmentEndpoint : Endpoint<RequestLcAmendmentEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public RequestLcAmendmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/amendments");
        Tag(Tags.LettersOfCredit);
        Summary("Request an LC amendment — DoA routed (BR-LC-10)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new RequestLcAmendmentCommand(
            req.LcId, req.ValueDelta, req.TenorIncreasing, req.ReasonCode, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public decimal? ValueDelta { get; set; }
        public bool TenorIncreasing { get; set; }
        public string ReasonCode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class ApproveLcAmendmentEndpoint : Endpoint<ApproveLcAmendmentEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public ApproveLcAmendmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/amendments/{amendmentId}/approve");
        Tag(Tags.LettersOfCredit);
        Summary("Approve an LC amendment");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new ApproveLcAmendmentCommand(req.LcId, req.AmendmentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public Guid AmendmentId { get; set; }
    }
}

internal sealed class PresentLcEndpoint : Endpoint<PresentLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public PresentLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/present");
        Tag(Tags.LettersOfCredit);
        Summary("Present shipping documents (BR-LC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new PresentLcCommand(
            req.LcId, req.PresentationNo, req.DocumentRefs), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public string PresentationNo { get; set; } = string.Empty;
        public List<string> DocumentRefs { get; set; } = new();
    }
}

internal sealed class LogLcDiscrepancyEndpoint : Endpoint<LogLcDiscrepancyEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public LogLcDiscrepancyEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/presentations/{presentationId}/discrepancies");
        Tag(Tags.LettersOfCredit);
        Summary("Log a document discrepancy — LC moves to discrepant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new LogLcDiscrepancyCommand(
            req.LcId, req.PresentationId, req.Code, req.Description), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public Guid PresentationId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

internal sealed class AcceptLcPresentationEndpoint : Endpoint<AcceptLcPresentationEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public AcceptLcPresentationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/presentations/{presentationId}/accept");
        Tag(Tags.LettersOfCredit);
        Summary("Accept presentation — creates maturity obligation per tenor (BR-LC-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new AcceptLcPresentationCommand(req.LcId, req.PresentationId, req.AcceptanceDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public Guid PresentationId { get; set; }
        public DateOnly AcceptanceDate { get; set; }
    }
}

internal sealed class RefuseLcPresentationEndpoint : Endpoint<RefuseLcPresentationEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public RefuseLcPresentationEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/presentations/{presentationId}/refuse");
        Tag(Tags.LettersOfCredit);
        Summary("Refuse a presentation");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new RefuseLcPresentationCommand(req.LcId, req.PresentationId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public Guid PresentationId { get; set; }
    }
}

internal sealed class RetireLcEndpoint : Endpoint<RetireLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public RetireLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/retire");
        Tag(Tags.LettersOfCredit);
        Summary("Retire the LC — margin release + realized FX (BR-LC-07)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new RetireLcCommand(req.LcId, req.RealizedFxRate, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public decimal? RealizedFxRate { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class CloseExpiredLcEndpoint : Endpoint<CloseExpiredLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public CloseExpiredLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/close-expired");
        Tag(Tags.LettersOfCredit);
        Summary("Close an expired-undrawn LC — margin + facility release (BR-LC-09)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(
            new CloseExpiredLcCommand(req.LcId, req.AsOfDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public DateOnly AsOfDate { get; set; }
    }
}

internal sealed class AddLcChargeEndpoint : Endpoint<AddLcChargeEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public AddLcChargeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/charges");
        Tag(Tags.LettersOfCredit);
        Summary("Record an LC charge (BR-LC-08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new AddLcChargeCommand(
            req.LcId, req.Type, req.Amount, req.Currency, req.RefDoc), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public LcChargeType Type { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? RefDoc { get; set; }
    }
}

internal sealed class CancelLcEndpoint : Endpoint<CancelLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public CancelLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/lcs/{lcId}/cancel");
        Tag(Tags.LettersOfCredit);
        Summary("Cancel an LC");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.SendAsync(new CancelLcCommand(req.LcId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetLcEndpoint : Endpoint<GetLcEndpoint.Request, LetterOfCreditResponse>
{
    private readonly IMediator _mediator;

    public GetLcEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/lcs/{lcId}");
        Tag(Tags.LettersOfCredit);
        Summary("Get an LC by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<LetterOfCreditResponse> result = await _mediator.QueryAsync(new GetLcQuery(req.LcId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid LcId { get; set; }
    }
}

internal sealed class ListLcsEndpoint : Endpoint<ListLcsEndpoint.Request, IReadOnlyList<LetterOfCreditResponse>>
{
    private readonly IMediator _mediator;

    public ListLcsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/lcs");
        Tag(Tags.LettersOfCredit);
        Summary("List all LCs for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<LetterOfCreditResponse>> result = await _mediator.QueryAsync(new ListLcsQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class CreateTtEndpoint : Endpoint<CreateTtEndpoint.Request, TtPaymentResponse>
{
    private readonly IMediator _mediator;

    public CreateTtEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tts");
        Tag(Tags.TelegraphicTransfers);
        Summary("Create a telegraphic transfer payment (BR-TT-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TtPaymentResponse> result = await _mediator.SendAsync(new CreateTtCommand(
            req.FileId, req.PoId, req.TtNumber, req.VendorId, req.BeneficiaryName, req.Currency,
            req.Amount, req.ScheduleType, req.BankRef), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid? FileId { get; set; }
        public Guid? PoId { get; set; }
        public string TtNumber { get; set; } = string.Empty;
        public Guid VendorId { get; set; }
        public string BeneficiaryName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public TtScheduleType ScheduleType { get; set; }
        public string BankRef { get; set; } = string.Empty;
    }
}

internal sealed class ExecuteTtEndpoint : Endpoint<ExecuteTtEndpoint.Request, TtPaymentResponse>
{
    private readonly IMediator _mediator;

    public ExecuteTtEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tts/{ttId}/execute");
        Tag(Tags.TelegraphicTransfers);
        Summary("Execute the TT — FX rate + charges captured (BR-TT-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TtPaymentResponse> result = await _mediator.SendAsync(new ExecuteTtCommand(
            req.TtId, req.ValueDate, req.FxRate, req.Charges), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid TtId { get; set; }
        public DateOnly ValueDate { get; set; }
        public decimal FxRate { get; set; }
        public decimal Charges { get; set; }
    }
}

internal sealed class MatchShipmentEndpoint : Endpoint<MatchShipmentEndpoint.Request, TtPaymentResponse>
{
    private readonly IMediator _mediator;

    public MatchShipmentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tts/{ttId}/match-shipment");
        Tag(Tags.TelegraphicTransfers);
        Summary("Match the TT to shipment (BR-TT-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TtPaymentResponse> result = await _mediator.SendAsync(new MatchShipmentCommand(req.TtId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid TtId { get; set; }
    }
}

internal sealed class CancelTtEndpoint : Endpoint<CancelTtEndpoint.Request, TtPaymentResponse>
{
    private readonly IMediator _mediator;

    public CancelTtEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/tts/{ttId}/cancel");
        Tag(Tags.TelegraphicTransfers);
        Summary("Cancel a TT");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TtPaymentResponse> result = await _mediator.SendAsync(new CancelTtCommand(req.TtId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid TtId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

internal sealed class GetTtEndpoint : Endpoint<GetTtEndpoint.Request, TtPaymentResponse>
{
    private readonly IMediator _mediator;

    public GetTtEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tts/{ttId}");
        Tag(Tags.TelegraphicTransfers);
        Summary("Get a TT by id");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<TtPaymentResponse> result = await _mediator.QueryAsync(new GetTtQuery(req.TtId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid TtId { get; set; }
    }
}

internal sealed class ListTtsEndpoint : Endpoint<ListTtsEndpoint.Request, IReadOnlyList<TtPaymentResponse>>
{
    private readonly IMediator _mediator;

    public ListTtsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/tts");
        Tag(Tags.TelegraphicTransfers);
        Summary("List all TTs for the tenant");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<TtPaymentResponse>> result = await _mediator.QueryAsync(new ListTtsQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class RegisterSwiftMessageEndpoint : Endpoint<RegisterSwiftMessageEndpoint.Request, SwiftMessageResponse>
{
    private readonly IMediator _mediator;

    public RegisterSwiftMessageEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/swift");
        Tag(Tags.SwiftRegistry);
        Summary("Register a SWIFT message (BR-TT-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<SwiftMessageResponse> result = await _mediator.SendAsync(new RegisterSwiftMessageCommand(
            req.MtType, req.Reference, req.Direction, req.LinkedLcId, req.LinkedTtId, req.ContentRef), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string MtType { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public Guid? LinkedLcId { get; set; }
        public Guid? LinkedTtId { get; set; }
        public string? ContentRef { get; set; }
    }
}

internal sealed class GetUnmatchedSwiftEndpoint : Endpoint<GetUnmatchedSwiftEndpoint.Request, IReadOnlyList<SwiftMessageResponse>>
{
    private readonly IMediator _mediator;

    public GetUnmatchedSwiftEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/swift/unmatched");
        Tag(Tags.SwiftRegistry);
        Summary("List unmatched SWIFT messages");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<SwiftMessageResponse>> result = await _mediator.QueryAsync(new GetUnmatchedSwiftQuery(), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
    }
}

internal sealed class CreateBankFacilityEndpoint : Endpoint<CreateBankFacilityEndpoint.Request, BankFacilityResponse>
{
    private readonly IMediator _mediator;

    public CreateBankFacilityEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/facilities");
        Tag(Tags.Facilities);
        Summary("Register a bank facility limit (BR-LC-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BankFacilityResponse> result = await _mediator.SendAsync(
            new CreateBankFacilityCommand(req.BankId, req.LimitAmount, req.Currency), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BankId { get; set; }
        public decimal LimitAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}

internal sealed class GetFacilityEndpoint : Endpoint<GetFacilityEndpoint.Request, BankFacilityResponse>
{
    private readonly IMediator _mediator;

    public GetFacilityEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/facilities/{bankId}");
        Tag(Tags.Facilities);
        Summary("Get facility exposure for a bank");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<BankFacilityResponse> result = await _mediator.QueryAsync(new GetFacilityQuery(req.BankId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid BankId { get; set; }
    }
}

internal sealed class GetObligationsEndpoint : Endpoint<GetObligationsEndpoint.Request, IReadOnlyList<PaymentObligationResponse>>
{
    private readonly IMediator _mediator;

    public GetObligationsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/obligations");
        Tag(Tags.Obligations);
        Summary("Get the payment obligation calendar window (BR-OBL-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<IReadOnlyList<PaymentObligationResponse>> result = await _mediator.QueryAsync(
            new GetObligationsQuery(req.From, req.To), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
    }
}

internal sealed class MarkObligationsOverdueEndpoint : Endpoint<MarkObligationsOverdueEndpoint.Request>
{
    private readonly IMediator _mediator;

    public MarkObligationsOverdueEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/obligations/mark-overdue");
        Tag(Tags.Obligations);
        Summary("Mark open obligations overdue as of a date (BR-OBL-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new MarkObligationsOverdueCommand(req.AsOfDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public DateOnly AsOfDate { get; set; }
    }
}