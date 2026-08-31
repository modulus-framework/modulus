using FluentAssertions;
using TradeFlow.Modules.Inventory.Domain.Entities;

namespace TradeFlow.Modules.Inventory.UnitTests;

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

    [Fact]
    public void Batch_Consume_ReducesQuantity()
    {
        var batch = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-1", "IMP-1", 100m,
            new DateOnly(2026, 12, 31), 10m);

        batch.Consume(30m);

        batch.Quantity.Should().Be(70m);
    }

    [Fact]
    public void Batch_Consume_ExceedingQuantity_Throws()
    {
        var batch = Batch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B-1", "IMP-1", 10m,
            new DateOnly(2026, 12, 31), 10m);

        var act = () => batch.Consume(20m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QcInspection_AcceptedTotal_SumsAcceptedQty()
    {
        var tenantId = Guid.NewGuid();
        var grnId = Guid.NewGuid();
        var inspection = QcInspection.Create(tenantId, grnId, new DateOnly(2026, 6, 1), "inspector");
        var grnLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        inspection.AddLine(grnLineId, itemId, 100m, 95m, QcDecision.Accepted, null);
        inspection.AddLine(grnLineId, itemId, 50m, 48m, QcDecision.Accepted, null);

        inspection.AcceptedTotal.Should().Be(143m);
    }

    [Fact]
    public void QcInspection_RejectedLine_ZeroAcceptedQty()
    {
        var tenantId = Guid.NewGuid();
        var grnId = Guid.NewGuid();
        var inspection = QcInspection.Create(tenantId, grnId, new DateOnly(2026, 6, 1), "inspector");

        inspection.AddLine(Guid.NewGuid(), Guid.NewGuid(), 100m, 0m, QcDecision.Rejected, "Damaged");

        inspection.Lines.Should().ContainSingle();
        inspection.Lines.First().Decision.Should().Be(QcDecision.Rejected);
        inspection.AcceptedTotal.Should().Be(0m);
    }

    [Fact]
    public void GrnReturnDraft_Submit_SetsStatusAndDebitNote()
    {
        var draft = GrnReturnDraft.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "admin");
        draft.AddLine(Guid.NewGuid(), Guid.NewGuid(), 10m, 15m, "Defective");

        var result = draft.Submit("DN-2026-001");

        result.IsSuccess.Should().BeTrue();
        draft.Status.Should().Be(ReturnDraftStatus.Submitted);
        draft.DebitNoteNumber.Should().Be("DN-2026-001");
        draft.TotalCreditAmount.Should().Be(150m);
    }

    [Fact]
    public void GrnReturnDraft_SubmitWithoutLines_Fails()
    {
        var draft = GrnReturnDraft.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "admin");

        var result = draft.Submit("DN-001");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReturnDraft.NoLines");
    }

    [Fact]
    public void GrnReturnDraft_SubmitTwice_Fails()
    {
        var draft = GrnReturnDraft.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "admin");
        draft.AddLine(Guid.NewGuid(), Guid.NewGuid(), 5m, 10m, "Reason");
        draft.Submit("DN-001");

        var result = draft.Submit("DN-002");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Grn_HoldForQc_TransitionsToQcHeld()
    {
        var grn = Grn.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "GRN-1", new DateOnly(2026, 6, 1), "buyer");

        grn.HoldForQc();

        grn.Status.Should().Be(GrnStatus.QcHeld);
    }

    [Fact]
    public void Issue_NegativeQuantity_Fails()
    {
        StockItem item = NewItem();
        item.Receive(100m, 10m);

        var result = item.Issue(-5m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stock.Qty");
    }

    [Fact]
    public void Revalue_NegativeCost_Throws()
    {
        StockItem item = NewItem();
        item.Receive(100m, 10m);

        var act = () => item.Revalue(-5m);

        act.Should().Throw<ArgumentException>();
    }
}