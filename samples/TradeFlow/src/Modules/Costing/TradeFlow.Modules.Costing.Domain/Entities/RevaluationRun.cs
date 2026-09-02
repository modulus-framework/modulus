using TradeFlow.Modules.Costing.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Domain.Entities;

public enum RevaluationRunStatus
{
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// Periodic landed-cost FX revaluation run (BR-LCS-10 audit trail). One run per
/// period close; records per-element FX variances on finalized cost sheets so the
/// P&L impact (FX gain/loss) is traceable to source documents. Never mutates the
/// cost sheets themselves — historical figures stay immutable (LCS-10).
/// </summary>
public sealed class RevaluationRun : AggregateRoot
{
    private readonly List<RevaluationVariance> _variances = new();

    private RevaluationRun() { }

    private RevaluationRun(Guid id, Guid tenantId, DateOnly periodEnd)
    {
        Id = id;
        TenantId = tenantId;
        PeriodEnd = periodEnd;
        Status = RevaluationRunStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public RevaluationRunStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public int SheetsScanned { get; private set; }
    public decimal TotalOriginalValueBdt { get; private set; }
    public decimal TotalRevaluedValueBdt { get; private set; }
    public decimal TotalFxGainLossBdt { get; private set; }

    public IReadOnlyList<RevaluationVariance> Variances => _variances;

    public static RevaluationRun Start(Guid tenantId, DateOnly periodEnd)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required", nameof(tenantId));
        return new RevaluationRun(Guid.NewGuid(), tenantId, periodEnd);
    }

    /// <summary>Records one element's FX variance (only while the run is open).</summary>
    public void AddVariance(Guid sheetId, string sheetNumber, Guid elementId, string elementName,
        string currency, decimal originalAmountFcy, decimal originalFxRate, decimal originalAmountBdt,
        decimal newFxRate, decimal newAmountBdt)
    {
        if (Status != RevaluationRunStatus.InProgress)
            throw new InvalidOperationException("Variances can only be added while the run is in progress");
        if (sheetId == Guid.Empty)
            throw new ArgumentException("Sheet id is required", nameof(sheetId));
        if (string.IsNullOrWhiteSpace(sheetNumber))
            throw new ArgumentException("Sheet number is required", nameof(sheetNumber));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (newFxRate <= 0m)
            throw new ArgumentException("New FX rate must be positive", nameof(newFxRate));

        _variances.Add(new RevaluationVariance(Guid.NewGuid(), sheetId, sheetNumber.Trim(), elementId,
            elementName.Trim(), currency.Trim().ToUpperInvariant(), originalAmountFcy, originalFxRate,
            originalAmountBdt, newFxRate, newAmountBdt));
    }

    /// <summary>Freezes the run: computes totals and raises the summary domain event.</summary>
    public void Complete(int sheetsScanned)
    {
        if (Status != RevaluationRunStatus.InProgress)
            throw new InvalidOperationException("Run is already completed");
        if (sheetsScanned < 0)
            throw new ArgumentException("Sheets scanned cannot be negative", nameof(sheetsScanned));

        SheetsScanned = sheetsScanned;
        TotalOriginalValueBdt = decimal.Round(_variances.Sum(v => v.OriginalAmountBdt), 4, MidpointRounding.ToEven);
        TotalRevaluedValueBdt = decimal.Round(_variances.Sum(v => v.NewAmountBdt), 4, MidpointRounding.ToEven);
        TotalFxGainLossBdt = decimal.Round(TotalRevaluedValueBdt - TotalOriginalValueBdt, 4, MidpointRounding.ToEven);
        CompletedAtUtc = DateTime.UtcNow;
        Status = RevaluationRunStatus.Completed;

        Raise(new LandedCostRevaluedDomainEvent(Id, TenantId, PeriodEnd, SheetsScanned,
            _variances.Count, TotalOriginalValueBdt, TotalRevaluedValueBdt, TotalFxGainLossBdt));
    }
}

/// <summary>One cost element's FX variance within a revaluation run (audit row).</summary>
public sealed class RevaluationVariance
{
    public RevaluationVariance(Guid id, Guid sheetId, string sheetNumber, Guid elementId, string elementName,
        string currency, decimal originalAmountFcy, decimal originalFxRate, decimal originalAmountBdt,
        decimal newFxRate, decimal newAmountBdt)
    {
        Id = id;
        SheetId = sheetId;
        SheetNumber = sheetNumber;
        ElementId = elementId;
        ElementName = elementName;
        Currency = currency;
        OriginalAmountFcy = originalAmountFcy;
        OriginalFxRate = originalFxRate;
        OriginalAmountBdt = originalAmountBdt;
        NewFxRate = newFxRate;
        NewAmountBdt = newAmountBdt;
        FxGainLossBdt = decimal.Round(newAmountBdt - originalAmountBdt, 4, MidpointRounding.ToEven);
    }

    public Guid Id { get; private set; }
    public Guid SheetId { get; private set; }
    public string SheetNumber { get; private set; } = null!;
    public Guid ElementId { get; private set; }
    public string ElementName { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal OriginalAmountFcy { get; private set; }
    public decimal OriginalFxRate { get; private set; }
    public decimal OriginalAmountBdt { get; private set; }
    public decimal NewFxRate { get; private set; }
    public decimal NewAmountBdt { get; private set; }
    public decimal FxGainLossBdt { get; private set; }
}