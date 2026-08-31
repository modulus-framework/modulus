using TradeFlow.Modules.TradeFinance.Domain.Events;
using TradeFlow.Modules.TradeFinance.Domain.Rules;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.TradeFinance.Domain.Entities;

/// <summary>
/// Letter of Credit aggregate (BR-LC-01..10). Runs the prerequisite gate
/// (BR-IMP-01/02), term consistency vs PO/PI (BR-LC-03, BR-PO-07), margin
/// block/release ledger (BR-LC-04, BR-MRG-01), presentations with discrepancy
/// workflow + maturity obligations (BR-LC-06), retirement with realized FX
/// (BR-LC-07), per-event charges (BR-LC-08) and expiry/shipment watchdogs
/// (BR-LC-09). Amendments follow the DoA (BR-LC-10).
/// </summary>
public sealed class LetterOfCredit : AggregateRoot
{
    private readonly List<LcCharge> _charges = new();
    private readonly List<LcAmendment> _amendments = new();
    private readonly List<LcPresentation> _presentations = new();
    private readonly List<MarginLedgerEntry> _marginLedger = new();
    private readonly List<MaturityObligation> _maturities = new();
    private LcPrerequisiteInput? _prerequisiteCheck;
    private IReadOnlyList<string> _termViolations = Array.Empty<string>();

    private LetterOfCredit() { }

    private LetterOfCredit(Guid id, Guid tenantId, Guid? fileId, Guid? poId, string lcNumber, LcType type,
        string currency, decimal amount, decimal tolerancePct, Guid applicantCompanyId, Guid beneficiaryVendorId,
        string beneficiaryName, Guid issuingBankId, DateOnly latestShipmentDate, DateOnly expiryDate,
        string incoterm, string portOfLoading, string portOfDischarge, bool partialShipmentAllowed,
        bool transshipmentAllowed, decimal marginPct, decimal bookingFxRate, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        PoId = poId;
        LcNumber = lcNumber;
        Type = type;
        Currency = currency;
        Amount = amount;
        TolerancePct = tolerancePct;
        ApplicantCompanyId = applicantCompanyId;
        BeneficiaryVendorId = beneficiaryVendorId;
        BeneficiaryName = beneficiaryName;
        IssuingBankId = issuingBankId;
        LatestShipmentDate = latestShipmentDate;
        ExpiryDate = expiryDate;
        Incoterm = incoterm;
        PortOfLoading = portOfLoading;
        PortOfDischarge = portOfDischarge;
        PartialShipmentAllowed = partialShipmentAllowed;
        TransshipmentAllowed = transshipmentAllowed;
        MarginPct = marginPct;
        BookingFxRate = bookingFxRate;
        CreatedBy = createdBy;
        Status = LcStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid? FileId { get; private set; }
    public Guid? PoId { get; private set; }
    public string LcNumber { get; private set; } = null!;
    public LcType Type { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public decimal TolerancePct { get; private set; }
    public Guid ApplicantCompanyId { get; private set; }
    public Guid BeneficiaryVendorId { get; private set; }
    public string BeneficiaryName { get; private set; } = null!;
    public Guid IssuingBankId { get; private set; }
    public DateOnly LatestShipmentDate { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public string Incoterm { get; private set; } = null!;
    public string PortOfLoading { get; private set; } = null!;
    public string PortOfDischarge { get; private set; } = null!;
    public bool PartialShipmentAllowed { get; private set; }
    public bool TransshipmentAllowed { get; private set; }
    public decimal MarginPct { get; private set; }
    public decimal BookingFxRate { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public LcStatus Status { get; private set; }
    public decimal? RealizedFxRate { get; private set; }
    public decimal MarginBlocked => _marginLedger.Sum(e => e.Type == MarginEventType.Block ? e.Amount : -e.Amount);
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<LcCharge> Charges => _charges;
    public IReadOnlyList<LcAmendment> Amendments => _amendments;
    public IReadOnlyList<LcPresentation> Presentations => _presentations;
    public IReadOnlyList<MarginLedgerEntry> MarginLedger => _marginLedger;
    public IReadOnlyList<MaturityObligation> Maturities => _maturities;
    public IReadOnlyList<string> TermViolations => _termViolations;

    public bool RequiresCfoApprovalForApplication => true;

    public static LetterOfCredit Create(Guid tenantId, Guid? fileId, Guid? poId, string lcNumber, LcType type,
        string currency, decimal amount, decimal tolerancePct, Guid applicantCompanyId, Guid beneficiaryVendorId,
        string beneficiaryName, Guid issuingBankId, DateOnly latestShipmentDate, DateOnly expiryDate,
        string incoterm, string portOfLoading, string portOfDischarge, bool partialShipmentAllowed,
        bool transshipmentAllowed, decimal marginPct, decimal bookingFxRate, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(lcNumber))
            throw new ArgumentException("LC number is required", nameof(lcNumber));
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "LC amount must be positive");
        if (expiryDate < latestShipmentDate)
            throw new ArgumentException("Expiry must be >= latest shipment date");
        if (marginPct < 0m || marginPct > 1m)
            throw new ArgumentOutOfRangeException(nameof(marginPct), "Margin must be in [0,1]");

        return new LetterOfCredit(Guid.NewGuid(), tenantId, fileId, poId, lcNumber.Trim(), type, currency.Trim(),
            amount, tolerancePct, applicantCompanyId, beneficiaryVendorId, beneficiaryName.Trim(),
            issuingBankId, latestShipmentDate, expiryDate, incoterm.Trim(), portOfLoading.Trim(),
            portOfDischarge.Trim(), partialShipmentAllowed, transshipmentAllowed, marginPct, bookingFxRate,
            createdBy.Trim());
    }

    /// <summary>
    /// Runs the BR-LC-01/BR-IMP-01/02 prerequisite gate and stores the result.
    /// Returns failures when unmet.
    /// </summary>
    public Result CheckPrerequisites(LcPrerequisiteInput input)
    {
        _prerequisiteCheck = input;
        IReadOnlyList<string> failures = LcPrerequisiteGate.Evaluate(input);
        if (failures.Count > 0)
            return Result.Failure(Error.BusinessRule("Lc.Prerequisites", string.Join("; ", failures)));
        return Result.Success();
    }

    /// <summary>Validates term consistency vs PO/PI and stores violations (BR-LC-03, BR-PO-07).</summary>
    public Result ValidateTerms(LcTermConsistencyInput input)
    {
        _termViolations = LcTermConsistencyValidator.Evaluate(input);
        if (_termViolations.Count > 0)
            return Result.Failure(Error.BusinessRule("Lc.Terms", string.Join("; ", _termViolations)));
        return Result.Success();
    }

    public Result SubmitForApproval()
    {
        if (Status != LcStatus.Draft)
            return Result.Failure(Error.BusinessRule("Lc.InvalidState", $"Only draft LCs can be submitted (status {Status})"));
        if (_prerequisiteCheck is null)
            return Result.Failure(Error.BusinessRule("Lc.PrerequisitesNotRun", "Prerequisites must be checked before application (BR-LC-01)"));
        if (_termViolations.Count > 0)
            return Result.Failure(Error.BusinessRule("Lc.TermViolations", "Term consistency violations must be resolved (BR-LC-03)"));

        Status = LcStatus.ApplicationPending;
        return Result.Success();
    }

    public Result ApproveApplication(string approvedBy)
    {
        if (Status != LcStatus.ApplicationPending)
            return Result.Failure(Error.BusinessRule("Lc.InvalidState", $"Only pending applications can be approved (status {Status})"));
        Status = LcStatus.ApplicationApproved;
        return Result.Success();
    }

    /// <summary>Issue the LC — margin is blocked (BR-LC-04) and the facility reserved by the caller.</summary>
    public Result Issue(string issuedBy, decimal facilityAvailable, bool facilityOverride)
    {
        if (Status != LcStatus.ApplicationApproved)
            return Result.Failure(Error.BusinessRule("Lc.InvalidState", $"Only approved applications can be issued (status {Status})"));
        if (facilityAvailable < Amount && !facilityOverride)
            return Result.Failure(Error.BusinessRule("Lc.FacilityInsufficient",
                "Facility exposure insufficient; CFO override required (BR-LC-05)"));

        decimal marginAmount = Math.Round(Amount * MarginPct, 2, MidpointRounding.ToEven);
        _marginLedger.Add(new MarginLedgerEntry(Guid.NewGuid(), MarginEventType.Block, marginAmount, Currency,
            IssuingBankId, "LC opening margin block (BR-LC-04)", DateOnly.FromDateTime(DateTime.UtcNow)));

        Status = LcStatus.Issued;
        Raise(new LcIssuedDomainEvent(Id, TenantId, LcNumber, Amount, Currency, marginAmount));
        return Result.Success();
    }

    /// <summary>Records an amendment request with DoA routing (BR-LC-10).</summary>
    public Result RequestAmendment(decimal? valueDelta, bool tenorIncreasing, string reasonCode, string reason,
        string requestedBy)
    {
        if (Status is LcStatus.Retired or LcStatus.Expired or LcStatus.Closed or LcStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Lc.Closed", "A closed LC cannot be amended"));

        AmendmentDoa doa = valueDelta > 0m || tenorIncreasing ? AmendmentDoa.Cfo : AmendmentDoa.ImportManager;
        var amendment = new LcAmendment(Guid.NewGuid(), _amendments.Count + 1, valueDelta, tenorIncreasing,
            reasonCode, reason, doa, requestedBy);
        _amendments.Add(amendment);
        return Result.Success();
    }

    public Result ApproveAmendment(Guid amendmentId, string approvedBy)
    {
        LcAmendment? amendment = _amendments.FirstOrDefault(a => a.Id == amendmentId);
        if (amendment is null)
            return Result.Failure(Error.NotFound("Lc.Amendment.NotFound", "Amendment not found"));

        amendment.Approve(approvedBy);
        if (amendment.ValueDelta > 0m)
            Amount += amendment.ValueDelta.Value;

        Raise(new LcAmendedDomainEvent(Id, TenantId, LcNumber, amendment.Version));
        return Result.Success();
    }

    public Result Present(string presentationNo, IReadOnlyList<string> documentRefs, string presentedBy)
    {
        if (Status is not (LcStatus.Issued or LcStatus.Presented or LcStatus.Discrepant))
            return Result.Failure(Error.BusinessRule("Lc.InvalidState", $"Presentations only accepted on issued LCs (status {Status})"));

        var presentation = new LcPresentation(Guid.NewGuid(), presentationNo, DateTime.UtcNow, documentRefs);
        _presentations.Add(presentation);
        Status = LcStatus.Presented;
        return Result.Success();
    }

    public Result LogDiscrepancy(Guid presentationId, string code, string description)
    {
        LcPresentation? presentation = _presentations.FirstOrDefault(p => p.Id == presentationId);
        if (presentation is null)
            return Result.Failure(Error.NotFound("Lc.Presentation.NotFound", "Presentation not found"));

        presentation.LogDiscrepancy(code, description);
        Status = LcStatus.Discrepant;
        return Result.Success();
    }

    /// <summary>Acceptance creates a maturity obligation per tenor (BR-LC-06).</summary>
    public Result AcceptPresentation(Guid presentationId, DateOnly acceptanceDate)
    {
        LcPresentation? presentation = _presentations.FirstOrDefault(p => p.Id == presentationId);
        if (presentation is null)
            return Result.Failure(Error.NotFound("Lc.Presentation.NotFound", "Presentation not found"));

        presentation.Accept();
        Status = LcStatus.Accepted;

        DateOnly dueDate = acceptanceDate.AddDays(TenorDays(Type));
        _maturities.Add(new MaturityObligation(Guid.NewGuid(), dueDate, Amount, Currency));
        Raise(new LcAcceptedDomainEvent(Id, TenantId, LcNumber, dueDate, Amount));
        return Result.Success();
    }

    public Result RefusePresentation(Guid presentationId)
    {
        LcPresentation? presentation = _presentations.FirstOrDefault(p => p.Id == presentationId);
        if (presentation is null)
            return Result.Failure(Error.NotFound("Lc.Presentation.NotFound", "Presentation not found"));

        presentation.Refuse();
        Status = LcStatus.Refused;
        return Result.Success();
    }

    /// <summary>Retire the LC — margin released, realized FX captured vs booking rate (BR-LC-07).</summary>
    public Result Retire(decimal? realizedFxRate, string reason, string by)
    {
        if (Status is not (LcStatus.Accepted or LcStatus.Issued or LcStatus.Presented or LcStatus.Discrepant))
            return Result.Failure(Error.BusinessRule("Lc.InvalidState", $"LC cannot be retired in status {Status}"));

        if (realizedFxRate.HasValue)
            RealizedFxRate = realizedFxRate.Value;

        decimal margin = _marginLedger.Sum(e => e.Type == MarginEventType.Block ? e.Amount : -e.Amount);
        if (margin > 0m)
            _marginLedger.Add(new MarginLedgerEntry(Guid.NewGuid(), MarginEventType.Release, margin, Currency,
                IssuingBankId, "Margin released at retirement (BR-LC-04)", DateOnly.FromDateTime(DateTime.UtcNow)));

        foreach (MaturityObligation maturity in _maturities.Where(m => m.Status == MaturityStatus.Open))
            maturity.Settle();

        Status = LcStatus.Retired;
        Raise(new LcRetiredDomainEvent(Id, TenantId, LcNumber, RealizedFxRate ?? BookingFxRate, BookingFxRate));
        return Result.Success();
    }

    public void AddCharge(LcChargeType type, decimal amount, string currency, string? refDoc)
    {
        _charges.Add(new LcCharge(Guid.NewGuid(), type, amount, currency, refDoc, DateTime.UtcNow));
    }

    /// <summary>
    /// Closes an expired-undrawn LC, releasing margin and facility (BR-LC-09).
    /// </summary>
    public Result CloseExpired(DateOnly asOfDate)
    {
        if (asOfDate <= ExpiryDate)
            return Result.Failure(Error.BusinessRule("Lc.NotExpired", "LC has not expired yet"));

        if (Status is LcStatus.Draft or LcStatus.ApplicationPending or LcStatus.ApplicationApproved)
        {
            Status = LcStatus.Expired;
        }
        else if (Status == LcStatus.Issued)
        {
            decimal margin = _marginLedger.Sum(e => e.Type == MarginEventType.Block ? e.Amount : -e.Amount);
            if (margin > 0m)
                _marginLedger.Add(new MarginLedgerEntry(Guid.NewGuid(), MarginEventType.Release, margin, Currency,
                    IssuingBankId, "Expired-undrawn closure releases margin (BR-LC-09)", DateOnly.FromDateTime(DateTime.UtcNow)));
            Status = LcStatus.Expired;
        }
        else
        {
            return Result.Failure(Error.BusinessRule("Lc.NotUndrawn", "Only undrawn LCs can be closed as expired-undrawn"));
        }

        Raise(new LcExpiredDomainEvent(Id, TenantId, LcNumber));
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is LcStatus.Retired or LcStatus.Closed or LcStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Lc.Closed", "A retired/closed LC cannot be cancelled"));
        Status = LcStatus.Cancelled;
        CancellationReason = reason;
        return Result.Success();
    }

    public static int TenorDays(LcType type) => type switch
    {
        LcType.Sight => 0,
        LcType.Usance30 => 30,
        LcType.Usance60 => 60,
        LcType.Usance90 => 90,
        LcType.Usance120 => 120,
        LcType.Usance180 => 180,
        LcType.Usance360 => 360,
        LcType.Upas => 90,
        _ => 0,
    };
}