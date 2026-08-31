using FluentAssertions;
using TradeFlow.Modules.Import.Domain.Entities;
using TradeFlow.Modules.Import.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.UnitTests;

[Trait("Category", "Unit")]
public sealed class ImportContainerTests
{
    [Theory]
    [InlineData("MSCU1234566")]
    [InlineData("MSKU1234565")]
    [InlineData("TCLU1234568")]
    public void ValidContainer_Accepts(string containerNo)
    {
        var container = new ImportContainer(Guid.NewGuid(), Guid.NewGuid(), containerNo, "40HC", "22G1", "SEAL-1");

        container.ContainerNo.Should().Be(containerNo.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("MSCU12345")]
    [InlineData("MSCU12345668")]
    [InlineData("MSCU1234567")]
    [InlineData("MSCUABCD567")]
    public void InvalidContainer_Throws(string containerNo)
    {
        var act = () => new ImportContainer(Guid.NewGuid(), Guid.NewGuid(), containerNo, "40HC", "22G1", "SEAL-1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ISO 6346*");
    }

    [Fact]
    public void Land_StartsDemurrageClock()
    {
        var container = new ImportContainer(Guid.NewGuid(), Guid.NewGuid(), "MSCU1234566", "40HC", "22G1", null);
        DateOnly landing = new(2026, 6, 1);

        container.Land(landing, 4);

        container.FreeDaysEnd.Should().Be(new DateOnly(2026, 6, 5));
        container.DemurrageDays(new DateOnly(2026, 6, 6)).Should().Be(1);
        container.DemurrageDays(new DateOnly(2026, 6, 3)).Should().Be(0);
    }

    [Fact]
    public void Demurrage_70PercentConsumed_RaisesAlertGate()
    {
        var container = new ImportContainer(Guid.NewGuid(), Guid.NewGuid(), "MSCU1234566", "40HC", "22G1", null);
        DateOnly landing = new(2026, 6, 1);
        container.Land(landing, 4);

        container.Consumed70Percent(new DateOnly(2026, 6, 4), 4).Should().BeTrue();

        container.RaiseAlert();
        container.Consumed70Percent(new DateOnly(2026, 6, 5), 4).Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public sealed class ImportDocumentTests
{
    [Fact]
    public void ProformaInvoice_ReconcileWithinTolerance_Matches()
    {
        var pi = ProformaInvoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PI-1", "USD",
            "Beneficiary", "Bank", "ACC", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "buyer");
        Guid lineId = Guid.NewGuid();
        pi.AddLine(new ProformaInvoiceLine(Guid.NewGuid(), pi.Id, lineId, null, "Widget", 100m, "PCS", 50m));

        Result result = pi.ReconcileToPo(lineId, 100m, 50m, 0.02m);

        result.IsSuccess.Should().BeTrue();
        pi.Status.Should().Be(DocumentReconciliationStatus.Matched);
    }

    [Fact]
    public void ProformaInvoice_ReconcileBeyondTolerance_LogsVariance()
    {
        var pi = ProformaInvoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PI-1", "USD",
            "Beneficiary", "Bank", "ACC", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "buyer");
        Guid lineId = Guid.NewGuid();
        pi.AddLine(new ProformaInvoiceLine(Guid.NewGuid(), pi.Id, lineId, null, "Widget", 100m, "PCS", 60m));

        Result result = pi.ReconcileToPo(lineId, 100m, 50m, 0.02m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pi.Variance");
        pi.Status.Should().Be(DocumentReconciliationStatus.VariancesLogged);
    }

    [Fact]
    public void ProformaInvoice_AcceptForLc_RequiresMatch()
    {
        var pi = ProformaInvoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PI-1", "USD",
            "Beneficiary", "Bank", "ACC", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "buyer");

        Result result = pi.AcceptForLc();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pi.NotMatched");
    }

    [Fact]
    public void CommercialInvoice_ReconcilesToPiWithinTolerance()
    {
        var ci = CommercialInvoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CI-1", "USD",
            10_000m, new DateOnly(2026, 2, 1), "buyer");

        Result result = ci.ReconcileToPi(10_000m, 0.02m);

        result.IsSuccess.Should().BeTrue();
        ci.Status.Should().Be(DocumentReconciliationStatus.Matched);
    }

    [Fact]
    public void Shipment_IsLcBreachRisk_TriggersAtT7()
    {
        var shipment = Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SHP-1", ShipmentMode.Sea,
            "VESSEL/V1", new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), "buyer");

        shipment.IsLcBreachRisk(new DateOnly(2026, 6, 5), new DateOnly(2026, 5, 29)).Should().BeTrue();

        shipment.AlertLcBreachRisk();
        shipment.IsLcBreachRisk(new DateOnly(2026, 6, 5), new DateOnly(2026, 5, 29)).Should().BeFalse();
    }

    [Fact]
    public void Permit_Draw_RespectsCeiling()
    {
        var permit = new ImportPermit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PM-1", "HS-Category",
            1_000m, 500_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "office");

        permit.Draw(Guid.NewGuid(), 600m, 300_000m, new DateOnly(2026, 2, 1)).IsSuccess.Should().BeTrue();
        permit.Draw(Guid.NewGuid(), 500m, 300_000m, new DateOnly(2026, 3, 1)).IsFailure.Should().BeTrue();
        permit.DrawnQty.Should().Be(600m);
    }
}

[Trait("Category", "Unit")]
public sealed class CnfAgentTests
{
    [Fact]
    public void ChargeBill_WithinRateCard_Verifies()
    {
        var agent = CnfAgent.Create(Guid.NewGuid(), "Alpha C&F", "AIN-001", "contacts");
        agent.SetRateCard(5_000m, 2_000m, 0.01m, 1_000m);

        Result result = agent.AddChargeBill(Guid.NewGuid(), "BILL-1", 4_500m, 5_000m);

        result.IsSuccess.Should().BeTrue();
        agent.ChargeBills.Should().ContainSingle(b => b.BillNo == "BILL-1" && b.Verified);
    }

    [Fact]
    public void ChargeBill_AboveRateCard_FlagsVariance()
    {
        var agent = CnfAgent.Create(Guid.NewGuid(), "Alpha C&F", "AIN-001", "contacts");
        agent.SetRateCard(5_000m, 2_000m, 0.01m, 1_000m);

        Result result = agent.AddChargeBill(Guid.NewGuid(), "BILL-2", 6_500m, 5_000m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnf.Variance");
        agent.ChargeBills.Should().ContainSingle(b => b.BillNo == "BILL-2" && !b.Verified);
    }
}
