using FluentAssertions;
using TradeFlow.Modules.Import.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.UnitTests;

[Trait("Category", "Unit")]
public sealed class ImportFileStateMachineTests
{
    private static ImportFile NewFile() =>
        ImportFile.Create(Guid.NewGuid(), Guid.NewGuid(), 2026, 1, null, "CIF", "USD",
            "Shanghai", "Chittagong", 100_000m, "buyer");

    [Fact]
    public void Create_SetsPlanned_AndGeneratesFileNumber()
    {
        var file = NewFile();

        file.Status.Should().Be(ImportFileStatus.Planned);
        file.FileNumber.Should().Be($"IMP-{file.CompanyId:N}-2026-0001");
        file.Sequence.Should().Be(1);
        file.FiscalYear.Should().Be(2026);
    }

    [Fact]
    public void LinkPo_RequiresPlanned_AndMovesToPoLinked()
    {
        var file = NewFile();

        Result result = file.LinkPo(Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        file.Status.Should().Be(ImportFileStatus.PoLinked);
        file.PoId.Should().NotBeNull();
    }

    [Fact]
    public void AcceptPi_WithoutPoLink_Fails()
    {
        var file = NewFile();

        Result result = file.AcceptPi(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Import.InvalidState");
    }

    [Fact]
    public void HappyPath_ReachesClosed()
    {
        var file = NewFile();
        Guid po = Guid.NewGuid(), pi = Guid.NewGuid(), lc = Guid.NewGuid(),
             shipment = Guid.NewGuid(), boe = Guid.NewGuid();

        file.LinkPo(po).IsSuccess.Should().BeTrue();
        file.AcceptPi(pi).IsSuccess.Should().BeTrue();
        file.Instrument(lc, null).IsSuccess.Should().BeTrue();
        file.Status.Should().Be(ImportFileStatus.FinanceInstrumented);
        file.StartProduction().IsSuccess.Should().BeTrue();
        file.MarkShipped(shipment).IsSuccess.Should().BeTrue();
        file.PresentToBank().IsSuccess.Should().BeTrue();
        file.ReleaseDocuments().IsSuccess.Should().BeTrue();
        file.ArriveAtPort(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.Should().BeTrue();
        file.UnderAssessment().IsSuccess.Should().BeTrue();
        file.MarkDutyPaid(boe).IsSuccess.Should().BeTrue();
        file.Release().IsSuccess.Should().BeTrue();
        file.DispatchInland().IsSuccess.Should().BeTrue();
        file.Receive().IsSuccess.Should().BeTrue();
        file.FinalizeCost().IsSuccess.Should().BeTrue();
        file.Close().IsSuccess.Should().BeTrue();

        file.Status.Should().Be(ImportFileStatus.Closed);
    }

    [Fact]
    public void Instrument_WithoutLcOrTt_Fails()
    {
        var file = NewFile();
        file.LinkPo(Guid.NewGuid());
        file.AcceptPi(Guid.NewGuid());

        Result result = file.Instrument(null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Import.Instrument");
    }

    [Fact]
    public void FinalizeCost_WithClearingBalance_IsBlocked()
    {
        var file = NewFile();
        file.LinkPo(Guid.NewGuid());
        file.AcceptPi(Guid.NewGuid());
        file.Instrument(Guid.NewGuid(), null);
        file.StartProduction();
        file.MarkShipped(Guid.NewGuid());
        file.PresentToBank();
        file.ReleaseDocuments();
        file.ArriveAtPort(DateOnly.FromDateTime(DateTime.UtcNow));
        file.UnderAssessment();
        file.MarkDutyPaid(Guid.NewGuid());
        file.Release();
        file.DispatchInland();
        file.Receive();

        file.AddCostEntry(new ImportCostEntry(Guid.NewGuid(), file.Id, "Duty", 0m, 5000m, "BDT",
            "Challan", Guid.NewGuid(), "CH-1", CostDirection.Debit));

        Result result = file.FinalizeCost();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Import.ClearingBalance");
    }

    [Fact]
    public void Close_FromUncostedState_Fails()
    {
        var file = NewFile();
        file.LinkPo(Guid.NewGuid());

        Result result = file.Close();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Hold_Resume_RoundTrip_ReturnsToPlanned()
    {
        var file = NewFile();

        file.Hold("waiting on supplier").IsSuccess.Should().BeTrue();
        file.Status.Should().Be(ImportFileStatus.Held);
        file.HoldReason.Should().Be("waiting on supplier");

        file.Resume().IsSuccess.Should().BeTrue();
        file.Status.Should().Be(ImportFileStatus.Planned);
    }

    [Fact]
    public void MarkDisputed_ThenCancel_AllowedBeforeClose()
    {
        var file = NewFile();

        file.MarkDisputed("discrepancy").IsSuccess.Should().BeTrue();
        file.DisputeReason.Should().Be("discrepancy");
        file.Status.Should().Be(ImportFileStatus.Disputed);

        file.Cancel("no longer needed").IsSuccess.Should().BeTrue();
        file.CancellationReason.Should().Be("no longer needed");
        file.Status.Should().Be(ImportFileStatus.Cancelled);
    }
}