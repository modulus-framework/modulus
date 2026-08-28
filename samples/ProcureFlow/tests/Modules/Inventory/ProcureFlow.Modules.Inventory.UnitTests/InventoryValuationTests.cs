using FluentAssertions;
using ProcureFlow.Modules.Inventory.Domain.Entities;

namespace ProcureFlow.Modules.Inventory.UnitTests;

[Trait("Category", "Unit")]
public sealed class InventoryValuationTests
{
    private static StockItem NewItem() =>
        StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-1", "Widget", "PCS");

    [Fact]
    public void WeightedAverage_UpdatesOnReceipt()
    {
        StockItem item = NewItem();

        item.Receive(100m, 10m);
        item.Receive(100m, 20m);

        item.QuantityOnHand.Should().Be(200m);
        item.WeightedAverageCost.Should().Be(15m);
        item.InventoryValue.Should().Be(3000m);
    }

    [Fact]
    public void Receive_WithZeroQuantity_Throws()
    {
        StockItem item = NewItem();

        var act = () => item.Receive(0m, 10m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_ReducesQuantityAtWeightedAverage()
    {
        StockItem item = NewItem();
        item.Receive(100m, 10m);
        item.Receive(100m, 20m);

        item.Issue(50m).IsSuccess.Should().BeTrue();

        item.QuantityOnHand.Should().Be(150m);
        item.WeightedAverageCost.Should().Be(15m);
    }

    [Fact]
    public void Issue_MoreThanOnHand_Fails()
    {
        StockItem item = NewItem();
        item.Receive(10m, 5m);

        var result = item.Issue(20m);

        result.IsFailure.Should().BeTrue();
        item.QuantityOnHand.Should().Be(10m);
    }

    [Fact]
    public void Revalue_OnLandedCostFinalization_PostsDelta()
    {
        StockItem item = NewItem();
        item.Receive(100m, 10m);

        decimal delta = item.Revalue(12m);

        delta.Should().Be(200m);
        item.WeightedAverageCost.Should().Be(12m);
        item.InventoryValue.Should().Be(1200m);
    }

    [Fact]
    public void Grn_OverReceiptBeyondTolerance_Throws()
    {
        var grn = Grn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "buyer");

        var act = () => grn.AddLine(Guid.NewGuid(), 100m, 120m, 0.05m, 10m, "PO-1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*over-receipt*");
    }

    [Fact]
    public void Grn_OverReceiptWithinTolerance_Accepted()
    {
        var grn = Grn.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "buyer");

        grn.AddLine(Guid.NewGuid(), 100m, 104m, 0.05m, 10m, "PO-1");

        grn.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Batch_FefoSuggestion_OrdersByExpiry()
    {
        var near = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-NEAR", "IMP-1", 10m,
            new DateOnly(2026, 7, 1), 10m);
        var far = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-FAR", "IMP-1", 10m,
            new DateOnly(2027, 1, 1), 10m);

        DateOnly today = new(2026, 6, 1);
        near.DaysToExpiry(today).Should().BeLessThan(far.DaysToExpiry(today));
    }

    [Fact]
    public void Batch_WithoutExpiry_SortsLast()
    {
        var expiring = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-1", "IMP-1", 10m,
            new DateOnly(2026, 7, 1), 10m);
        var none = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-2", "IMP-1", 10m, null, 10m);

        expiring.DaysToExpiry(new DateOnly(2026, 6, 1)).Should().Be(30);
        none.DaysToExpiry(new DateOnly(2026, 6, 1)).Should().Be(int.MaxValue);
    }
}