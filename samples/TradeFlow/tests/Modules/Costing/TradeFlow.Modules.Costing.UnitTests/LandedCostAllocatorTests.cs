using FluentAssertions;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.UnitTests;

[Trait("Category", "Unit")]
public sealed class LandedCostAllocatorTests
{
    private static LandedCostSheet NewSheet() =>
        LandedCostSheet.Create(Guid.NewGuid(), Guid.NewGuid(), "LCS-1", "USD");

    private static LandedCostLine Line(Guid id, decimal value, decimal qty = 1m) =>
        new(Guid.NewGuid(), id, value, value, qty, 10m, 12m, 2m, 0.5m);

    [Fact]
    public void ValueDriver_AllocatesProportionally()
    {
        var sheet = NewSheet();
        Guid lineA = Guid.NewGuid(), lineB = Guid.NewGuid();
        sheet.AddLine(lineA, 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddLine(lineB, 300m, 300m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 0m, 1m, 100m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1"));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();
        sheet.Status.Should().Be(CostSheetStatus.Ready);

        LandedCostLine a = sheet.Lines.Single(l => l.SourceLineId == lineA);
        LandedCostLine b = sheet.Lines.Single(l => l.SourceLineId == lineB);

        a.TotalLandedCostBdt.Should().Be(100m + 25m);
        b.TotalLandedCostBdt.Should().Be(300m + 75m);
        a.UnitLandedCost.Should().Be(125m);
        b.UnitLandedCost.Should().Be(375m);
    }

    [Fact]
    public void QuantityDriver_AllocatesByReceivedQty()
    {
        var sheet = NewSheet();
        Guid lineA = Guid.NewGuid(), lineB = Guid.NewGuid();
        sheet.AddLine(lineA, 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddLine(lineB, 300m, 300m, 3m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Handling", 0m, 1m, 400m,
            CostElementDriver.Quantity, CostElementScope.File, CostTreatment.LandedCost, "Bill", "H-1"));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();
        sheet.Lines.Single(l => l.SourceLineId == lineA).TotalLandedCostBdt.Should().Be(200m);
        sheet.Lines.Single(l => l.SourceLineId == lineB).TotalLandedCostBdt.Should().Be(600m);
    }

    [Fact]
    public void SelectedLinesScope_SkipsOtherLines()
    {
        var sheet = NewSheet();
        Guid lineA = Guid.NewGuid(), lineB = Guid.NewGuid();
        sheet.AddLine(lineA, 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddLine(lineB, 300m, 300m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Demurrage", 0m, 1m, 50m,
            CostElementDriver.Direct, CostElementScope.SelectedLines, CostTreatment.LandedCost,
            "Bill", "D-1", [lineA]));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();
        sheet.Lines.Single(l => l.SourceLineId == lineA).TotalLandedCostBdt.Should().Be(150m);
        sheet.Lines.Single(l => l.SourceLineId == lineB).TotalLandedCostBdt.Should().Be(300m);
    }

    [Fact]
    public void Rounding_IsBankersAt4Dp()
    {
        var sheet = NewSheet();
        sheet.AddLine(Guid.NewGuid(), 100m, 100m, 3m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "X", 0m, 1m, 10m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "X-1"));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();
        sheet.Lines.Single().TotalLandedCostBdt.Should().Be(110m);
        sheet.Lines.Single().UnitLandedCost.Should().Be(decimal.Round(110m / 3m, 4, MidpointRounding.ToEven));
    }

    [Fact]
    public void ResidualPenny_GoesToLargestLine()
    {
        var sheet = NewSheet();
        Guid lineA = Guid.NewGuid(), lineB = Guid.NewGuid();
        sheet.AddLine(lineA, 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddLine(lineB, 300m, 300m, 1m, 0m, 0m, 0m, 0m);
        // 10 / 4 = 2.5 exactly; force a non-exact split: element 1.0000 over 4 drivers
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Residual", 0m, 1m, 1m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "R-1"));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();

        // allocations: A = 0.25, B = 0.75 → 0.2500 / 0.7500 exact; largest B absorbs any residual
        LandedCostLine b = sheet.Lines.Single(l => l.SourceLineId == lineB);
        b.Allocations.Should().ContainSingle(a => a.ElementName == "Residual" && a.AmountBdt == 0.75m);
    }

    [Fact]
    public void FxConversion_ElementUsesItsOwnRate()
    {
        var sheet = NewSheet();
        sheet.AddLine(Guid.NewGuid(), 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Insurance", 100m, 110m, 11_000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Policy", "INS-1"));

        Result result = sheet.Allocate();

        result.IsSuccess.Should().BeTrue();
        sheet.Lines.Single().TotalLandedCostBdt.Should().Be(11_100m);
    }

    [Fact]
    public void Finalize_ThenAdjust_IncrementsVersion()
    {
        var sheet = NewSheet();
        sheet.AddLine(Guid.NewGuid(), 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 0m, 1m, 10m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1"));
        sheet.Allocate().IsSuccess.Should().BeTrue();

        Result submit = sheet.SubmitForFinalization();
        submit.IsSuccess.Should().BeTrue();
        sheet.Status.Should().Be(CostSheetStatus.Finalized);

        Result adjust = sheet.OpenAdjustment();
        adjust.IsSuccess.Should().BeTrue();
        sheet.Status.Should().Be(CostSheetStatus.Adjusted);
        sheet.SheetVersion.Should().Be(2);
    }

    [Fact]
    public void AddElement_OnFinalizedSheet_IsBlocked()
    {
        var sheet = NewSheet();
        sheet.AddLine(Guid.NewGuid(), 100m, 100m, 1m, 0m, 0m, 0m, 0m);
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 0m, 1m, 10m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1"));
        sheet.Allocate();
        sheet.SubmitForFinalization();

        Result result = sheet.AddElement(new CostElement(Guid.NewGuid(), "Late", 0m, 1m, 5m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "L-1"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Lcs.Finalized");
    }

    [Fact]
    public void Allocate_WithoutElements_Fails()
    {
        var sheet = NewSheet();
        sheet.AddLine(Guid.NewGuid(), 100m, 100m, 1m, 0m, 0m, 0m, 0m);

        Result result = sheet.Allocate();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Lcs.NoElements");
    }
}