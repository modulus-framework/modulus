using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.Domain.Entities;

/// <summary>
/// Bill of Entry — customs clearance entity (BR-CC-01..05).
/// Mirrors ASYCUDA fields; status: submitted → queried → assessed → paid → examined → released.
/// </summary>
public enum BoeStatus
{
    Draft = 1,
    Submitted = 2,
    Queried = 3,
    Assessed = 4,
    Paid = 5,
    Examined = 6,
    Released = 7,
    Disputed = 8,
}

public enum BoeLane
{
    Green = 1,
    Yellow = 2,
    Red = 3,
}

public sealed class BillOfEntry : AggregateRoot
{
    private readonly List<BoeLine> _lines = new();
    private readonly List<BoeDutyLine> _dutyLines = new();
    private readonly List<BoeMilestone> _milestones = new();

    private BillOfEntry() { }

    public BillOfEntry(Guid id, Guid tenantId, Guid fileId, string boeNumber, DateOnly boeDate,
        string customsOffice, Guid? cnfAgentId, BoeLane lane, string declarantAin)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        BoeNumber = boeNumber;
        BoeDate = boeDate;
        CustomsOffice = customsOffice;
        CnfAgentId = cnfAgentId;
        Lane = lane;
        DeclarantAin = declarantAin;
        Status = BoeStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public string BoeNumber { get; private set; } = null!;
    public DateOnly BoeDate { get; private set; }
    public string CustomsOffice { get; private set; } = null!;
    public Guid? CnfAgentId { get; private set; }
    public BoeLane Lane { get; private set; }
    public string DeclarantAin { get; private set; } = null!;
    public BoeStatus Status { get; private set; }
    public decimal TotalAssessableValue { get; private set; }
    public decimal TotalDuty { get; private set; }
    public DateOnly? AssessedAt { get; private set; }
    public DateOnly? PaidAt { get; private set; }
    public DateOnly? ReleasedAt { get; private set; }
    public string? DisputeReason { get; private set; }

    public IReadOnlyList<BoeLine> Lines => _lines;
    public IReadOnlyList<BoeDutyLine> DutyLines => _dutyLines;
    public IReadOnlyList<BoeMilestone> Milestones => _milestones;

    public static BillOfEntry Create(Guid tenantId, Guid fileId, string boeNumber, DateOnly boeDate,
        string customsOffice, Guid? cnfAgentId, BoeLane lane, string declarantAin)
    {
        if (string.IsNullOrWhiteSpace(boeNumber))
            throw new ArgumentException("BoE number is required", nameof(boeNumber));
        return new BillOfEntry(Guid.NewGuid(), tenantId, fileId, boeNumber.Trim(), boeDate,
            customsOffice.Trim(), cnfAgentId, lane, declarantAin.Trim());
    }

    public void AddLine(Guid? ciLineId, string hsCode, decimal assessableValue, decimal quantity, string uom)
    {
        _lines.Add(new BoeLine(Guid.NewGuid(), ciLineId, hsCode, assessableValue, quantity, uom));
        RecalculateTotals();
    }

    public void AddDutyLine(string component, decimal rate, decimal amount, string? sroRef = null)
    {
        _dutyLines.Add(new BoeDutyLine(Guid.NewGuid(), component, rate, amount, sroRef));
        TotalDuty = _dutyLines.Sum(d => d.Amount);
    }

    public Result Submit()
    {
        if (Status != BoeStatus.Draft)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only draft BoE can be submitted"));
        Status = BoeStatus.Submitted;
        AddMilestone("Submitted", DateTime.UtcNow);
        return Result.Success();
    }

    public Result RecordQuery(string reason)
    {
        if (Status != BoeStatus.Submitted)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only submitted BoE can be queried"));
        Status = BoeStatus.Queried;
        DisputeReason = reason;
        AddMilestone($"Queried: {reason}", DateTime.UtcNow);
        return Result.Success();
    }

    public Result RecordAssessment()
    {
        if (Status != BoeStatus.Submitted && Status != BoeStatus.Queried)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only submitted/queried BoE can be assessed"));
        Status = BoeStatus.Assessed;
        AssessedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        AddMilestone("Assessed", DateTime.UtcNow);
        return Result.Success();
    }

    public Result RecordPayment()
    {
        if (Status != BoeStatus.Assessed)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only assessed BoE can be marked paid"));
        Status = BoeStatus.Paid;
        PaidAt = DateOnly.FromDateTime(DateTime.UtcNow);
        AddMilestone("Paid", DateTime.UtcNow);
        return Result.Success();
    }

    public Result RecordExamination(BoeLane newLane)
    {
        if (Status != BoeStatus.Paid)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only paid BoE can be examined"));
        Status = BoeStatus.Examined;
        Lane = newLane;
        AddMilestone($"Examined - Lane {newLane}", DateTime.UtcNow);
        return Result.Success();
    }

    public Result Release()
    {
        if (Status != BoeStatus.Examined)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only examined BoE can be released"));
        Status = BoeStatus.Released;
        ReleasedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        AddMilestone("Released", DateTime.UtcNow);
        return Result.Success();
    }

    public Result RaiseDispute(string reason)
    {
        if (Status != BoeStatus.Assessed && Status != BoeStatus.Examined)
            return Result.Failure(Error.BusinessRule("BoE.Status", "Only assessed/examined BoE can be disputed"));
        Status = BoeStatus.Disputed;
        DisputeReason = reason;
        AddMilestone($"Disputed: {reason}", DateTime.UtcNow);
        return Result.Success();
    }

    private void AddMilestone(string name, DateTime atUtc)
    {
        _milestones.Add(new BoeMilestone(Guid.NewGuid(), name, atUtc));
    }

    private void RecalculateTotals()
    {
        TotalAssessableValue = _lines.Sum(l => l.AssessableValue);
    }
}

public sealed class BoeLine
{
    public BoeLine(Guid id, Guid? ciLineId, string hsCode, decimal assessableValue, decimal quantity, string uom)
    {
        Id = id;
        CiLineId = ciLineId;
        HsCode = hsCode;
        AssessableValue = assessableValue;
        Quantity = quantity;
        Uom = uom;
    }

    public Guid Id { get; private set; }
    public Guid? CiLineId { get; private set; }
    public string HsCode { get; private set; } = null!;
    public decimal AssessableValue { get; private set; }
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
}

public sealed class BoeDutyLine
{
    public BoeDutyLine(Guid id, string component, decimal rate, decimal amount, string? sroRef)
    {
        Id = id;
        Component = component;
        Rate = rate;
        Amount = amount;
        SroRef = sroRef;
    }

    public Guid Id { get; private set; }
    public string Component { get; private set; } = null!;
    public decimal Rate { get; private set; }
    public decimal Amount { get; private set; }
    public string? SroRef { get; private set; }
}

public sealed class BoeMilestone
{
    public BoeMilestone(Guid id, string name, DateTime atUtc)
    {
        Id = id;
        Name = name;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
}

/// <summary>
/// Assessment variance — system-computed vs. customs-assessed difference (BR-CC-03).
/// </summary>
public enum VarianceType
{
    Classification = 1,
    Valuation = 2,
    SroDenial = 3,
    Quantity = 4,
    Other = 5,
}

public enum VarianceStatus
{
    Open = 1,
    UnderReview = 2,
    Resolved = 3,
    Accepted = 4,
}

public sealed class AssessmentVariance : AggregateRoot
{
    private AssessmentVariance() { }

    public AssessmentVariance(Guid id, Guid tenantId, Guid boeId, Guid boeLineId,
        VarianceType type, string component, decimal systemAmount, decimal assessedAmount, string reason)
    {
        Id = id;
        TenantId = tenantId;
        BoeId = boeId;
        BoeLineId = boeLineId;
        Type = type;
        Component = component;
        SystemAmount = systemAmount;
        AssessedAmount = assessedAmount;
        VarianceAmount = assessedAmount - systemAmount;
        Reason = reason;
        Status = VarianceStatus.Open;
    }

    public Guid TenantId { get; private set; }
    public Guid BoeId { get; private set; }
    public Guid BoeLineId { get; private set; }
    public VarianceType Type { get; private set; }
    public string Component { get; private set; } = null!;
    public decimal SystemAmount { get; private set; }
    public decimal AssessedAmount { get; private set; }
    public decimal VarianceAmount { get; private set; }
    public string Reason { get; private set; } = null!;
    public VarianceStatus Status { get; private set; }
    public string? Resolution { get; private set; }

    public static AssessmentVariance Create(Guid tenantId, Guid boeId, Guid boeLineId,
        VarianceType type, string component, decimal systemAmount, decimal assessedAmount, string reason)
    {
        return new AssessmentVariance(Guid.NewGuid(), tenantId, boeId, boeLineId,
            type, component, systemAmount, assessedAmount, reason.Trim());
    }

    public Result Resolve(string resolution)
    {
        if (Status == VarianceStatus.Resolved || Status == VarianceStatus.Accepted)
            return Result.Failure(Error.BusinessRule("Variance.Status", "Variance already resolved/accepted"));
        Status = VarianceStatus.Resolved;
        Resolution = resolution;
        return Result.Success();
    }

    public Result Accept()
    {
        if (Status != VarianceStatus.Open && Status != VarianceStatus.UnderReview)
            return Result.Failure(Error.BusinessRule("Variance.Status", "Only open/under-review variances can be accepted"));
        Status = VarianceStatus.Accepted;
        return Result.Success();
    }
}

/// <summary>
/// Port charges — demurrage, detention, port dues, examination charges (BR-CC-04).
/// </summary>
public enum PortChargeType
{
    Demurrage = 1,
    Detention = 2,
    PortDues = 3,
    Examination = 4,
    DeliveryOrder = 5,
    GateOut = 6,
    Other = 7,
}

public sealed class PortCharge : AggregateRoot
{
    private PortCharge() { }

    public PortCharge(Guid id, Guid tenantId, Guid fileId, PortChargeType chargeType,
        decimal amount, string currency, string? receiptRef, DateOnly chargedOn, string? description)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        ChargeType = chargeType;
        Amount = amount;
        Currency = currency;
        ReceiptRef = receiptRef;
        ChargedOn = chargedOn;
        Description = description;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public PortChargeType ChargeType { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? ReceiptRef { get; private set; }
    public DateOnly ChargedOn { get; private set; }
    public string? Description { get; private set; }

    public static PortCharge Create(Guid tenantId, Guid fileId, PortChargeType chargeType,
        decimal amount, string currency, DateOnly chargedOn, string? description = null)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        return new PortCharge(Guid.NewGuid(), tenantId, fileId, chargeType,
            amount, currency.Trim(), null, chargedOn, description);
    }
}
