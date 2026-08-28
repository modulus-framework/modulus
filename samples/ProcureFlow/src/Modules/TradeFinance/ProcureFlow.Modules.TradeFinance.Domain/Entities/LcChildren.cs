namespace ProcureFlow.Modules.TradeFinance.Domain.Entities;

public enum LcType
{
    Sight = 1,
    Usance30 = 2,
    Usance60 = 3,
    Usance90 = 4,
    Usance120 = 5,
    Usance180 = 6,
    Usance360 = 7,
    Upas = 8,
}

public enum LcStatus
{
    Draft = 1,
    ApplicationPending = 2,
    ApplicationApproved = 3,
    Issued = 4,
    Presented = 5,
    Accepted = 6,
    Discrepant = 7,
    Refused = 8,
    Retired = 9,
    Expired = 10,
    Closed = 11,
    Cancelled = 12,
}

public enum LcChargeType
{
    Opening = 1,
    Amendment = 2,
    Acceptance = 3,
    Swift = 4,
    Confirmation = 5,
    Handling = 6,
}

public enum AmendmentDoa
{
    Cfo = 1,
    ImportManager = 2,
}

public enum MarginEventType
{
    Block = 1,
    Release = 2,
    Adjust = 3,
    TopUp = 4,
}

public enum PresentationStatus
{
    Presented = 1,
    Accepted = 2,
    Refused = 3,
}

public enum MaturityStatus
{
    Open = 1,
    Settled = 2,
    Overdue = 3,
}

/// <summary>Bank charge captured per event → file cost ledger (BR-LC-08).</summary>
public sealed class LcCharge
{
    public LcCharge(Guid id, LcChargeType type, decimal amount, string currency, string? refDoc, DateTime atUtc)
    {
        Id = id;
        Type = type;
        Amount = amount;
        Currency = currency;
        RefDoc = refDoc;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public LcChargeType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? RefDoc { get; private set; }
    public DateTime AtUtc { get; private set; }
}

/// <summary>LC amendment — value/tenor-increasing → CFO, clerical → Import Mgr (BR-LC-10).</summary>
public sealed class LcAmendment
{
    private LcAmendment() { }

    public LcAmendment(Guid id, int version, decimal? valueDelta, bool tenorIncreasing, string reasonCode,
        string reason, AmendmentDoa doa, string requestedBy)
    {
        Id = id;
        Version = version;
        ValueDelta = valueDelta;
        TenorIncreasing = tenorIncreasing;
        ReasonCode = reasonCode;
        Reason = reason;
        Doa = doa;
        RequestedBy = requestedBy;
        Approved = false;
    }

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public decimal? ValueDelta { get; private set; }
    public bool TenorIncreasing { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public AmendmentDoa Doa { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public bool Approved { get; private set; }
    public string? ApprovedBy { get; private set; }

    public void Approve(string by)
    {
        if (Approved)
            throw new InvalidOperationException("Amendment already approved");
        Approved = true;
        ApprovedBy = by;
    }
}

/// <summary>Presentation of documents (BR-LC-06).</summary>
public sealed class LcPresentation
{
    private readonly List<LcDiscrepancy> _discrepancies = new();

    public LcPresentation(Guid id, string presentationNo, DateTime presentedAtUtc, IReadOnlyList<string> documentRefs)
    {
        Id = id;
        PresentationNo = presentationNo;
        PresentedAtUtc = presentedAtUtc;
        _documentRefs = documentRefs.ToList();
        Status = PresentationStatus.Presented;
    }

    public Guid Id { get; private set; }
    public string PresentationNo { get; private set; } = null!;
    public DateTime PresentedAtUtc { get; private set; }
    private readonly List<string> _documentRefs = new();
    public IReadOnlyList<string> DocumentRefs => _documentRefs;
    public PresentationStatus Status { get; private set; }

    public IReadOnlyList<LcDiscrepancy> Discrepancies => _discrepancies;

    public void LogDiscrepancy(string code, string description)
    {
        _discrepancies.Add(new LcDiscrepancy(Guid.NewGuid(), code, description));
        Status = PresentationStatus.Presented;
    }

    public void Accept() => Status = PresentationStatus.Accepted;
    public void Refuse() => Status = PresentationStatus.Refused;
}

/// <summary>Discrepancy notice with code list (BR-LC-06).</summary>
public sealed class LcDiscrepancy
{
    public LcDiscrepancy(Guid id, string code, string description)
    {
        Id = id;
        Code = code;
        Description = description;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;
}

/// <summary>Margin ledger entry — restricted cash (BR-LC-04, BR-MRG-01).</summary>
public sealed class MarginLedgerEntry
{
    public MarginLedgerEntry(Guid id, MarginEventType type, decimal amount, string currency, Guid bankId, string reason, DateOnly bookedOn)
    {
        Id = id;
        Type = type;
        Amount = amount;
        Currency = currency;
        BankId = bankId;
        Reason = reason;
        BookedOn = bookedOn;
    }

    public Guid Id { get; private set; }
    public MarginEventType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public Guid BankId { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateOnly BookedOn { get; private set; }
}

/// <summary>Maturity obligation (bill) created on acceptance per tenor (BR-LC-06).</summary>
public sealed class MaturityObligation
{
    public MaturityObligation(Guid id, DateOnly dueDate, decimal amount, string currency)
    {
        Id = id;
        DueDate = dueDate;
        Amount = amount;
        Currency = currency;
        Status = MaturityStatus.Open;
    }

    public Guid Id { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public MaturityStatus Status { get; private set; }

    public void Settle() => Status = MaturityStatus.Settled;
    public void MarkOverdue() => Status = MaturityStatus.Overdue;
}