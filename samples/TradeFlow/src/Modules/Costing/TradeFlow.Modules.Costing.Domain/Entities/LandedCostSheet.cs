using TradeFlow.Modules.Costing.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Domain.Entities;

public enum CostSheetStatus
{
    Draft = 1,
    Accumulating = 2,
    Ready = 3,
    Finalized = 4,
    Adjusted = 5,
}

public enum CostElementDriver
{
    Value = 1,
    Quantity = 2,
    NetWeight = 3,
    GrossWeight = 4,
    VolumeCbm = 5,
    ContainerShare = 6,
    Direct = 7,
    ManualPercent = 8,
}

public enum CostElementScope
{
    File = 1,
    SelectedLines = 2,
}

public enum CostTreatment
{
    LandedCost = 1,
    Recoverable = 2,
    AdvanceAsset = 3,
}

/// <summary>
/// Landed Cost Sheet (BR-LCS-01..10). Auto-created with the import file;
/// elements staged, allocated per BR-LCS-06/07, finalized by Import Mgr →
/// Finance Head approval (BR-LCS-08). Late bills become an adjustment version
/// (BR-LCS-09).
/// </summary>
public sealed class LandedCostSheet : AggregateRoot
{
    private readonly List<LandedCostLine> _lines = new();
    private readonly List<CostElement> _elements = new();

    private LandedCostSheet() { }

    private LandedCostSheet(Guid id, Guid tenantId, Guid fileId, string sheetNumber, string currency)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        SheetNumber = sheetNumber;
        Currency = currency;
        Status = CostSheetStatus.Draft;
        SheetVersion = 1;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public string SheetNumber { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public CostSheetStatus Status { get; private set; }
    public int SheetVersion { get; private set; }
    public Guid? FinalizedBy { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }

    public IReadOnlyList<LandedCostLine> Lines => _lines;
    public IReadOnlyList<CostElement> Elements => _elements;

    public static LandedCostSheet Create(Guid tenantId, Guid fileId, string sheetNumber, string currency)
    {
        if (string.IsNullOrWhiteSpace(sheetNumber))
            throw new ArgumentException("Sheet number is required", nameof(sheetNumber));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        return new LandedCostSheet(Guid.NewGuid(), tenantId, fileId, sheetNumber.Trim(), currency.Trim());
    }

    public void AddLine(Guid sourceLineId, decimal goodsValueFcy, decimal goodsValueBdt, decimal receivedQty,
        decimal netWeightKg, decimal grossWeightKg, decimal volumeCbm, decimal containerShare)
    {
        if (receivedQty <= 0m)
            throw new ArgumentException("Received qty must be positive (BR-LCS-04)", nameof(receivedQty));
        _lines.Add(new LandedCostLine(Guid.NewGuid(), sourceLineId, goodsValueFcy, goodsValueBdt,
            receivedQty, netWeightKg, grossWeightKg, volumeCbm, containerShare));
    }

    public Result AddElement(CostElement element)
    {
        if (Status is CostSheetStatus.Finalized or CostSheetStatus.Adjusted)
            return Result.Failure(Error.BusinessRule("Lcs.Finalized", "Cannot stage elements on a finalized sheet (BR-LCS-09)"));

        if (element.Scope == CostElementScope.SelectedLines &&
            (element.SelectedLineIds is null || element.SelectedLineIds.Count == 0))
        {
            return Result.Failure(Error.Validation("Lcs.Scope", "Selected-lines scope requires at least one line"));
        }

        if (element.Scope == CostElementScope.SelectedLines &&
            element.SelectedLineIds!.Any(id => _lines.All(l => l.SourceLineId != id)))
        {
            return Result.Failure(Error.Validation("Lcs.Scope", "Selected line does not belong to this sheet"));
        }

        _elements.Add(element);
        Status = CostSheetStatus.Accumulating;
        return Result.Success();
    }

    /// <summary>Recompute per-line allocations with banker's rounding + residual to largest line (BR-LCS-06/07).</summary>
    public Result Allocate()
    {
        if (_elements.Count == 0)
            return Result.Failure(Error.BusinessRule("Lcs.NoElements", "No cost elements staged (BR-LCS-03)"));
        if (_lines.Count == 0)
            return Result.Failure(Error.BusinessRule("Lcs.NoLines", "No lines on the sheet (BR-LCS-04)"));

        foreach (LandedCostLine line in _lines)
            line.ClearAllocations();

        foreach (CostElement element in _elements)
        {
            IReadOnlyList<AllocatedCost> allocations = LandedCostAllocator.Allocate(element, _lines);
            foreach (AllocatedCost allocation in allocations)
            {
                LandedCostLine? line = _lines.FirstOrDefault(l => l.SourceLineId == allocation.LineId);
                line?.AddAllocation(new LineCostAllocation(Guid.NewGuid(), element.Id, element.Name,
                    allocation.AmountBdt, element.Treatment, allocation.IsResidual));
            }
        }

        foreach (LandedCostLine line in _lines)
            line.ComputeTotals();

        Status = CostSheetStatus.Ready;
        return Result.Success();
    }

    public Result SubmitForFinalization()
    {
        if (Status != CostSheetStatus.Ready)
            return Result.Failure(Error.BusinessRule("Lcs.NotReady", "Only a ready sheet can be submitted (BR-LCS-03)"));
        Status = CostSheetStatus.Finalized;
        FinalizedAtUtc = DateTime.UtcNow;
        Raise(new CostSheetFinalizedDomainEvent(Id, TenantId, FileId, SheetNumber, SheetVersion));
        return Result.Success();
    }

    /// <summary>Late bills post-finalization → adjustment version (BR-LCS-09).</summary>
    public Result OpenAdjustment()
    {
        if (Status is not (CostSheetStatus.Finalized or CostSheetStatus.Adjusted))
            return Result.Failure(Error.BusinessRule("Lcs.NotFinalized", "Adjustment requires a finalized sheet"));
        Status = CostSheetStatus.Adjusted;
        SheetVersion++;
        Raise(new CostSheetAdjustedDomainEvent(Id, TenantId, FileId, SheetNumber, SheetVersion));
        return Result.Success();
    }
}

/// <summary>Landed-cost line per received item line; goods value + allocations feed unit cost (BR-LCS-04).</summary>
public sealed class LandedCostLine
{
    private readonly List<LineCostAllocation> _allocations = new();

    public LandedCostLine(Guid id, Guid sourceLineId, decimal goodsValueFcy, decimal goodsValueBdt,
        decimal receivedQty, decimal netWeightKg, decimal grossWeightKg, decimal volumeCbm, decimal containerShare)
    {
        Id = id;
        SourceLineId = sourceLineId;
        GoodsValueFcy = goodsValueFcy;
        GoodsValueBdt = goodsValueBdt;
        ReceivedQty = receivedQty;
        NetWeightKg = netWeightKg;
        GrossWeightKg = grossWeightKg;
        VolumeCbm = volumeCbm;
        ContainerShare = containerShare;
    }

    public Guid Id { get; private set; }
    public Guid SourceLineId { get; private set; }
    public decimal GoodsValueFcy { get; private set; }
    public decimal GoodsValueBdt { get; private set; }
    public decimal ReceivedQty { get; private set; }
    public decimal NetWeightKg { get; private set; }
    public decimal GrossWeightKg { get; private set; }
    public decimal VolumeCbm { get; private set; }
    public decimal ContainerShare { get; private set; }
    public decimal TotalLandedCostBdt { get; private set; }
    public decimal UnitLandedCost { get; private set; }

    public IReadOnlyList<LineCostAllocation> Allocations => _allocations;

    public void ClearAllocations() => _allocations.Clear();

    public void AddAllocation(LineCostAllocation allocation) => _allocations.Add(allocation);

    public void ComputeTotals()
    {
        TotalLandedCostBdt = GoodsValueBdt + _allocations.Sum(a => a.AmountBdt);
        UnitLandedCost = decimal.Round(TotalLandedCostBdt / ReceivedQty, 4, MidpointRounding.ToEven);
    }
}

/// <summary>One staged cost element with its own document FX conversion and treatment (BR-LCS-07/10).</summary>
public sealed class CostElement
{
    public CostElement(Guid id, string name, decimal amountFcy, decimal fxRate, decimal amountBdt,
        CostElementDriver driver, CostElementScope scope, CostTreatment treatment,
        string sourceDocType, string sourceDocNumber, IReadOnlyList<Guid>? selectedLineIds = null,
        string? currency = null)
    {
        Id = id;
        Name = name;
        AmountFcy = amountFcy;
        FxRate = fxRate;
        AmountBdt = amountBdt;
        Driver = driver;
        Scope = scope;
        Treatment = treatment;
        SourceDocType = sourceDocType;
        SourceDocNumber = sourceDocNumber;
        SelectedLineIds = selectedLineIds;
        Currency = currency;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal AmountFcy { get; private set; }
    public decimal FxRate { get; private set; }
    public decimal AmountBdt { get; private set; }
    public CostElementDriver Driver { get; private set; }
    public CostElementScope Scope { get; private set; }
    public CostTreatment Treatment { get; private set; }
    public string SourceDocType { get; private set; } = null!;
    public string SourceDocNumber { get; private set; } = null!;
    public IReadOnlyList<Guid>? SelectedLineIds { get; private set; }

    /// <summary>FCY currency code of <see cref="AmountFcy"/>; null (or BDT) when the element is BDT-denominated.</summary>
    public string? Currency { get; private set; }
}

/// <summary>Allocated amount of one element onto one line (BR-LCS-07).</summary>
public sealed class LineCostAllocation
{
    public LineCostAllocation(Guid id, Guid elementId, string elementName, decimal amountBdt,
        CostTreatment treatment, bool isResidual)
    {
        Id = id;
        ElementId = elementId;
        ElementName = elementName;
        AmountBdt = amountBdt;
        Treatment = treatment;
        IsResidual = isResidual;
    }

    public Guid Id { get; private set; }
    public Guid ElementId { get; private set; }
    public string ElementName { get; private set; } = null!;
    public decimal AmountBdt { get; private set; }
    public CostTreatment Treatment { get; private set; }
    public bool IsResidual { get; private set; }
}

public sealed record AllocatedCost(Guid LineId, decimal AmountBdt, bool IsResidual);

/// <summary>
/// Deterministic landed-cost allocation engine (BR-LCS-06/07):
/// <c>allocated(element, line) = element_amount × driver(line) / Σ driver(lines in scope)</c>.
/// Banker's rounding at 4 dp; residual pennies to the largest line (tie-break by line id).
/// </summary>
public static class LandedCostAllocator
{
    public static IReadOnlyList<AllocatedCost> Allocate(CostElement element, IReadOnlyList<LandedCostLine> lines)
    {
        List<LandedCostLine> scope = element.Scope == CostElementScope.File
            ? lines.ToList()
            : lines.Where(l => element.SelectedLineIds!.Contains(l.SourceLineId)).ToList();

        if (scope.Count == 0)
            return Array.Empty<AllocatedCost>();

        if (element.Driver == CostElementDriver.Direct)
        {
            LandedCostLine target = scope[0];
            return [new AllocatedCost(target.SourceLineId, element.AmountBdt, false)];
        }

        decimal[] drivers = scope.Select(l => DriverValue(element.Driver, l)).ToArray();
        decimal sum = drivers.Sum();
        if (sum <= 0m)
            return Array.Empty<AllocatedCost>();

        var results = new List<AllocatedCost>(scope.Count);
        decimal allocatedTotal = 0m;

        for (int i = 0; i < scope.Count; i++)
        {
            decimal allocated = element.Driver == CostElementDriver.ManualPercent
                ? element.AmountBdt * (drivers[i] / 100m)
                : element.AmountBdt * drivers[i] / sum;

            decimal rounded = decimal.Round(allocated, 4, MidpointRounding.ToEven);
            results.Add(new AllocatedCost(scope[i].SourceLineId, rounded, false));
            allocatedTotal += rounded;
        }

        decimal residual = decimal.Round(element.AmountBdt - allocatedTotal, 4, MidpointRounding.ToEven);
        if (residual != 0m)
        {
            int largestIndex = IndexOfLargest(drivers);
            var largest = results[largestIndex];
            results[largestIndex] = new AllocatedCost(largest.LineId,
                decimal.Round(largest.AmountBdt + residual, 4, MidpointRounding.ToEven), true);
        }

        return results;
    }

    private static decimal DriverValue(CostElementDriver driver, LandedCostLine line) => driver switch
    {
        CostElementDriver.Value => line.GoodsValueBdt,
        CostElementDriver.Quantity => line.ReceivedQty,
        CostElementDriver.NetWeight => line.NetWeightKg,
        CostElementDriver.GrossWeight => line.GrossWeightKg,
        CostElementDriver.VolumeCbm => line.VolumeCbm,
        CostElementDriver.ContainerShare => line.ContainerShare,
        CostElementDriver.ManualPercent => 1m,
        _ => 0m,
    };

    /// <summary>Deterministic tie-break: largest driver value; equal values → smallest line id wins.</summary>
    private static int IndexOfLargest(decimal[] drivers)
    {
        int index = 0;
        for (int i = 1; i < drivers.Length; i++)
        {
            if (drivers[i] > drivers[index])
                index = i;
        }
        return index;
    }
}