namespace TradeFlow.Modules.Customs.Domain.Entities;

public enum BoeStatus
{
    Submitted = 1,
    Queried = 2,
    Assessed = 3,
    Paid = 4,
    Examined = 5,
    Released = 6,
}

public enum ExaminationLane
{
    Green = 1,
    Yellow = 2,
    Red = 3,
}

public enum DisputeResolutionType
{
    QueryResponse = 1,
    Appeal = 2,
    ProvisionalUnderGuarantee = 3,
}

public enum DisputeStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>One BoE line mirroring an ASYCUDA / CI line (BR-CUS-02).</summary>
public sealed class BoeLine
{
    private BoeLine() { }

    public BoeLine(Guid id, Guid? ciLineId, string hsCode, string description, decimal quantity, string uom,
        decimal declaredAvFcy, decimal customsExchangeRate, decimal landingChargePct)
    {
        Id = id;
        CiLineId = ciLineId;
        HsCode = hsCode;
        Description = description;
        Quantity = quantity;
        Uom = uom;
        DeclaredAvFcy = declaredAvFcy;
        CustomsExchangeRate = customsExchangeRate;
        LandingChargePct = landingChargePct;
    }

    public Guid Id { get; private set; }
    public Guid? CiLineId { get; private set; }
    public string HsCode { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;

    /// <summary>Declared assessable value in FCY for this line (BR-CUS-02).</summary>
    public decimal DeclaredAvFcy { get; private set; }

    /// <summary>NBR-notified monthly customs FX rate (distinct from bank booking rate — §23.1).</summary>
    public decimal CustomsExchangeRate { get; private set; }

    public decimal LandingChargePct { get; private set; }

    /// <summary>Tariff value floor in BDT (AV_effective = max(declared_AV, tariff_value), §23.1).</summary>
    public decimal? TariffValueBdt { get; private set; }

    public decimal? ComputedTtiBdt { get; private set; }
    public decimal? AssessedTtiBdt { get; private set; }

    private readonly List<AssessedDutyLine> _assessedDutyLines = new();
    public IReadOnlyList<AssessedDutyLine> AssessedDutyLines => _assessedDutyLines;

    private readonly List<RateLineageRow> _rateLineage = new();
    /// <summary>Rate-row ids used in the calculation — reproducibility (BR-DS-04).</summary>
    public IReadOnlyList<RateLineageRow> RateLineage => _rateLineage;

    public void SetTariffValue(decimal tariffValueBdt)
    {
        if (tariffValueBdt < 0m)
            throw new ArgumentOutOfRangeException(nameof(tariffValueBdt));
        TariffValueBdt = tariffValueBdt;
    }

    public void RecordComputed(decimal computedTtiBdt, IEnumerable<RateLineageRow> lineage)
    {
        ComputedTtiBdt = computedTtiBdt;
        _rateLineage.Clear();
        _rateLineage.AddRange(lineage);
    }

    public void Assess(decimal assessedTtiBdt, IEnumerable<AssessedDutyLine> dutyLines)
    {
        AssessedTtiBdt = assessedTtiBdt;
        _assessedDutyLines.Clear();
        _assessedDutyLines.AddRange(dutyLines);
    }
}

/// <summary>Per-component assessed duty amount (BR-CUS-02 duty lines).</summary>
public sealed class AssessedDutyLine
{
    public AssessedDutyLine(string component, decimal amount)
    {
        Component = component;
        Amount = amount;
    }

    public string Component { get; private set; }
    public decimal Amount { get; private set; }
}

/// <summary>One rate-row reference used in a computation (BR-DS-04).</summary>
public sealed class RateLineageRow
{
    public RateLineageRow(string component, Guid rateRowId, decimal rateUsed)
    {
        Component = component;
        RateRowId = rateRowId;
        RateUsed = rateUsed;
    }

    public string Component { get; private set; }
    public Guid RateRowId { get; private set; }
    public decimal RateUsed { get; private set; }
}

/// <summary>Challan register entry with scanned evidence (BR-CUS-06).</summary>
public sealed class Challan
{
    private Challan() { }

    public Challan(Guid id, string challanNo, decimal amount, DateTime paidAtUtc, string? evidenceRef)
    {
        Id = id;
        ChallanNo = challanNo;
        Amount = amount;
        PaidAtUtc = paidAtUtc;
        EvidenceRef = evidenceRef;
    }

    public Guid Id { get; private set; }
    public string ChallanNo { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
    public string? EvidenceRef { get; private set; }
}

/// <summary>Timestamped clearance milestone for SLA analytics (BR-CUS-05).</summary>
public sealed class ClearanceMilestone
{
    public ClearanceMilestone(string stage, DateTime occurredAtUtc)
    {
        Stage = stage;
        OccurredAtUtc = occurredAtUtc;
    }

    public string Stage { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

/// <summary>System-computed vs assessed variance per line (BR-CUS-03).</summary>
public sealed class DisputeRecord
{
    private DisputeRecord() { }

    public DisputeRecord(Guid id, Guid boeLineId, decimal varianceAmount, decimal tolerancePct,
        DisputeResolutionType resolutionType, string? guaranteeRef)
    {
        Id = id;
        BoeLineId = boeLineId;
        VarianceAmount = varianceAmount;
        TolerancePct = tolerancePct;
        ResolutionType = resolutionType;
        GuaranteeRef = guaranteeRef;
        Status = DisputeStatus.Open;
    }

    public Guid Id { get; private set; }
    public Guid BoeLineId { get; private set; }
    public decimal VarianceAmount { get; private set; }
    public decimal TolerancePct { get; private set; }
    public DisputeResolutionType ResolutionType { get; private set; }
    public string? GuaranteeRef { get; private set; }
    public DisputeStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public void Resolve(string? notes = null)
    {
        Status = DisputeStatus.Resolved;
        Notes = notes;
        ResolvedAt = DateTime.UtcNow;
    }
}