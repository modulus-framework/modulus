using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.Domain.Entities;

public enum ImportPlanStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Revised = 4,
    Closed = 5,
}

/// <summary>
/// Import Plan — annual/quarterly import plan by item-category with budget,
/// LC limit, and seasonality alignment (BR-IP-01..06). Feeds feasibility
/// baselines; plan vs. actual tracked as files close.
/// </summary>
public sealed class ImportPlan : AggregateRoot
{
    private readonly List<ImportPlanLine> _lines = new();

    private ImportPlan() { }

    private ImportPlan(Guid id, Guid tenantId, Guid companyId, int fiscalYear, string planNumber,
        DateOnly periodStart, DateOnly periodEnd, string currency)
    {
        Id = id;
        TenantId = tenantId;
        CompanyId = companyId;
        FiscalYear = fiscalYear;
        PlanNumber = planNumber;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Currency = currency;
        Status = ImportPlanStatus.Draft;
        PlanVersion = 1;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public int FiscalYear { get; private set; }
    public string PlanNumber { get; private set; } = null!;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public string Currency { get; private set; } = null!;
    public ImportPlanStatus Status { get; private set; }
    public int PlanVersion { get; private set; }
    public decimal TotalEstFob { get; private set; }
    public decimal TotalEstLanded { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<ImportPlanLine> Lines => _lines;

    public static ImportPlan Create(Guid tenantId, Guid companyId, int fiscalYear,
        DateOnly periodStart, DateOnly periodEnd, string currency)
    {
        if (periodEnd <= periodStart)
            throw new ArgumentException("Period end must be after period start", nameof(periodEnd));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        string planNumber = $"IMPPLAN-{companyId:N}-{fiscalYear}-{DateTime.UtcNow:yyyyMMdd}";
        return new ImportPlan(Guid.NewGuid(), tenantId, companyId, fiscalYear,
            planNumber, periodStart, periodEnd, currency.Trim());
    }

    public void AddLine(Guid? itemId, Guid? categoryId, string description,
        decimal estQty, decimal estFob, decimal estLanded, decimal? targetMonth,
        string? sourceCountry)
    {
        if (Status is ImportPlanStatus.Closed)
            throw new InvalidOperationException("Cannot add lines to a closed plan");

        if (targetMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(targetMonth), "Target month must be 1-12");

        _lines.Add(new ImportPlanLine(Guid.NewGuid(), itemId, categoryId, description,
            estQty, estFob, estLanded, targetMonth, sourceCountry));
        RecalculateTotals();
    }

    public void RemoveLine(Guid lineId)
    {
        if (Status is ImportPlanStatus.Closed)
            throw new InvalidOperationException("Cannot remove lines from a closed plan");

        ImportPlanLine? line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is not null)
        {
            _lines.Remove(line);
            RecalculateTotals();
        }
    }

    public Result Submit()
    {
        if (Status != ImportPlanStatus.Draft)
            return Result.Failure(Error.BusinessRule("Plan.Status", "Only draft plans can be submitted"));
        Status = ImportPlanStatus.Submitted;
        return Result.Success();
    }

    public Result Approve(Guid approvedBy)
    {
        if (Status != ImportPlanStatus.Submitted)
            return Result.Failure(Error.BusinessRule("Plan.Status", "Only submitted plans can be approved"));
        Status = ImportPlanStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Revise()
    {
        if (Status != ImportPlanStatus.Approved)
            return Result.Failure(Error.BusinessRule("Plan.Status", "Only approved plans can be revised"));
        Status = ImportPlanStatus.Revised;
        PlanVersion++;
        return Result.Success();
    }

    public Result Close()
    {
        if (Status is not (ImportPlanStatus.Approved or ImportPlanStatus.Revised))
            return Result.Failure(Error.BusinessRule("Plan.Status", "Only approved/revised plans can be closed"));
        Status = ImportPlanStatus.Closed;
        return Result.Success();
    }

    private void RecalculateTotals()
    {
        TotalEstFob = _lines.Sum(l => l.EstFob);
        TotalEstLanded = _lines.Sum(l => l.EstLanded);
    }
}

public sealed class ImportPlanLine
{
    private ImportPlanLine() { }

    public ImportPlanLine(Guid id, Guid? itemId, Guid? categoryId, string description,
        decimal estQty, decimal estFob, decimal estLanded, decimal? targetMonth, string? sourceCountry)
    {
        Id = id;
        ItemId = itemId;
        CategoryId = categoryId;
        Description = description;
        EstQty = estQty;
        EstFob = estFob;
        EstLanded = estLanded;
        TargetMonth = targetMonth;
        SourceCountry = sourceCountry;
        ActualQty = 0;
        ActualFob = 0;
        ActualLanded = 0;
    }

    public Guid Id { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal EstQty { get; private set; }
    public decimal EstFob { get; private set; }
    public decimal EstLanded { get; private set; }
    public decimal? TargetMonth { get; private set; }
    public string? SourceCountry { get; private set; }

    /// <summary>Actuals populated as files close (BR-IP-05).</summary>
    public decimal ActualQty { get; private set; }
    public decimal ActualFob { get; private set; }
    public decimal ActualLanded { get; private set; }

    public void RecordActual(decimal qty, decimal fob, decimal landed)
    {
        ActualQty += qty;
        ActualFob += fob;
        ActualLanded += landed;
    }
}
