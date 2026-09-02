using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradeFlow.Modules.Customs.Application;
using TradeFlow.Modules.Customs.Application.Duty.Commands;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Application.Duty.Queries;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Events;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.UnitTests;

[Trait("Category", "Unit")]
public sealed class AitAtLedgerEntryTests
{
    [Fact]
    public void CreateAddition_SucceedsWithoutEvents()
    {
        Guid companyId = Guid.NewGuid();
        DateOnly bookedOn = new(2026, 8, 15);

        AitAtLedgerEntry entry = AitAtLedgerEntry.CreateAddition(
            companyId, 2026, DutyComponent.Ait, 5000m, null, Guid.NewGuid(), bookedOn);

        entry.CompanyId.Should().Be(companyId);
        entry.Component.Should().Be(DutyComponent.Ait);
        entry.Amount.Should().Be(5000m);
        entry.EntryType.Should().Be(AitAtEntryType.Addition);
        entry.BookedOn.Should().Be(bookedOn);
        entry.ReturnPeriod.Should().BeNull();
        entry.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DutyComponent.Cd)]
    [InlineData(DutyComponent.Rd)]
    [InlineData(DutyComponent.Sd)]
    [InlineData(DutyComponent.Vat)]
    public void CreateAddition_NonAitAtComponent_Throws(DutyComponent component)
    {
        Action act = () => AitAtLedgerEntry.CreateAddition(
            Guid.NewGuid(), 2026, component, 100m, null, null, new DateOnly(2026, 8, 15));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Only AIT/AT components*");
    }

    [Fact]
    public void RecordAdjustment_SetsReturnPeriodAndRaisesEvent()
    {
        Guid companyId = Guid.NewGuid();
        DateOnly bookedOn = new(2026, 8, 15);

        AitAtLedgerEntry entry = AitAtLedgerEntry.RecordAdjustment(
            companyId, 2026, DutyComponent.At, 2000m, "2026-07", "Adjusted against return", bookedOn);

        entry.EntryType.Should().Be(AitAtEntryType.Adjustment);
        entry.ReturnPeriod.Should().Be("2026-07");
        entry.Narrative.Should().Be("Adjusted against return");
        entry.FileId.Should().BeNull();
        entry.BoeId.Should().BeNull();

        entry.DomainEvents.Should().HaveCount(1);
        entry.DomainEvents[0].Should().BeOfType<AitAtAdjustmentRecordedDomainEvent>();
        var @event = (AitAtAdjustmentRecordedDomainEvent)entry.DomainEvents[0];
        @event.EntryId.Should().Be(entry.Id);
        @event.CompanyId.Should().Be(companyId);
        @event.Component.Should().Be(DutyComponent.At);
        @event.Amount.Should().Be(2000m);
        @event.ReturnPeriod.Should().Be("2026-07");
    }

    [Fact]
    public void RecordAdjustment_MissingReturnPeriod_Throws()
    {
        Action act = () => AitAtLedgerEntry.RecordAdjustment(
            Guid.NewGuid(), 2026, DutyComponent.Ait, 100m, "  ", null, new DateOnly(2026, 8, 15));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Return period is required*");
    }

    [Fact]
    public void RecordAdjustment_NegativeAmount_Throws()
    {
        Action act = () => AitAtLedgerEntry.RecordAdjustment(
            Guid.NewGuid(), 2026, DutyComponent.Ait, -1m, "2026-07", null, new DateOnly(2026, 8, 15));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

[Trait("Category", "Unit")]
public sealed class RecordAitAtAdjustmentHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static Mock<IAitAtLedgerRepository> Repo(params AitAtLedgerEntry[] entries)
    {
        var entriesList = new List<AitAtLedgerEntry>(entries);
        var mock = new Mock<IAitAtLedgerRepository>();
        mock.Setup(r => r.GetForCompanyFyAsync(CompanyId, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AitAtLedgerEntry>)entriesList);
        mock.Setup(r => r.AddAsync(It.IsAny<AitAtLedgerEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AitAtLedgerEntry, CancellationToken>((e, _) => entriesList.Add(e))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static RecordAitAtAdjustmentHandler NewHandler(Mock<IAitAtLedgerRepository> repo, out Mock<IUnitOfWork> unitOfWork)
    {
        unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new RecordAitAtAdjustmentHandler(repo.Object, unitOfWork.Object);
    }

    [Fact]
    public async Task ValidAdjustment_PersistsAndCommits()
    {
        AitAtLedgerEntry addition = AitAtLedgerEntry.CreateAddition(
            CompanyId, 2026, DutyComponent.Ait, 5000m, null, Guid.NewGuid(), new DateOnly(2026, 7, 10));
        Mock<IAitAtLedgerRepository> repo = Repo(addition);
        RecordAitAtAdjustmentHandler handler = NewHandler(repo, out Mock<IUnitOfWork> unitOfWork);

        var command = new RecordAitAtAdjustmentCommand(
            CompanyId, 2026, DutyComponent.Ait, 3000m, "2026-07", "Q1 adjustment", new DateOnly(2026, 8, 15));

        Result<AitAtLedgerEntryResponse> result = await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EntryType.Should().Be(AitAtEntryType.Adjustment);
        result.Value.ReturnPeriod.Should().Be("2026-07");
        repo.Verify(r => r.AddAsync(It.IsAny<AitAtLedgerEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OverAdjustment_Blocked()
    {
        AitAtLedgerEntry addition = AitAtLedgerEntry.CreateAddition(
            CompanyId, 2026, DutyComponent.Ait, 1000m, null, Guid.NewGuid(), new DateOnly(2026, 7, 10));
        Mock<IAitAtLedgerRepository> repo = Repo(addition);
        RecordAitAtAdjustmentHandler handler = NewHandler(repo, out _);

        var command = new RecordAitAtAdjustmentCommand(
            CompanyId, 2026, DutyComponent.Ait, 1500m, "2026-07", null, new DateOnly(2026, 8, 15));

        Result<AitAtLedgerEntryResponse> result = await handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AitAt.OverAdjustment");
        repo.Verify(r => r.AddAsync(It.IsAny<AitAtLedgerEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdjustmentAgainstExistingAdjustments_OnlyUpToBalance()
    {
        AitAtLedgerEntry addition = AitAtLedgerEntry.CreateAddition(
            CompanyId, 2026, DutyComponent.At, 1000m, null, Guid.NewGuid(), new DateOnly(2026, 7, 10));
        AitAtLedgerEntry priorAdjustment = AitAtLedgerEntry.RecordAdjustment(
            CompanyId, 2026, DutyComponent.At, 600m, "2026-07", null, new DateOnly(2026, 7, 31));
        Mock<IAitAtLedgerRepository> repo = Repo(addition, priorAdjustment);
        RecordAitAtAdjustmentHandler handler = NewHandler(repo, out _);

        var withinBalance = new RecordAitAtAdjustmentCommand(
            CompanyId, 2026, DutyComponent.At, 400m, "2026-08", null, new DateOnly(2026, 8, 31));
        Result<AitAtLedgerEntryResponse> okResult = await handler.HandleAsync(withinBalance, CancellationToken.None);

        var beyondBalance = new RecordAitAtAdjustmentCommand(
            CompanyId, 2026, DutyComponent.At, 1m, "2026-08", null, new DateOnly(2026, 8, 31));
        Result<AitAtLedgerEntryResponse> overResult = await handler.HandleAsync(beyondBalance, CancellationToken.None);

        okResult.IsSuccess.Should().BeTrue();
        overResult.IsFailure.Should().BeTrue();
        overResult.Error.Code.Should().Be("AitAt.OverAdjustment");
    }

    [Fact]
    public async Task NonAitAtComponent_Blocked()
    {
        Mock<IAitAtLedgerRepository> repo = Repo();
        RecordAitAtAdjustmentHandler handler = NewHandler(repo, out _);

        var command = new RecordAitAtAdjustmentCommand(
            CompanyId, 2026, DutyComponent.Cd, 100m, "2026-07", null, new DateOnly(2026, 8, 15));

        Result<AitAtLedgerEntryResponse> result = await handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AitAt.Component");
    }
}

[Trait("Category", "Unit")]
public sealed class GetAitAtLedgerHandlerTests
{
    [Fact]
    public async Task ClosingBalance_SubtractsCounterpostedAdjustments()
    {
        Guid companyId = Guid.NewGuid();
        var entries = new List<AitAtLedgerEntry>
        {
            AitAtLedgerEntry.CreateAddition(companyId, 2026, DutyComponent.Ait, 5000m, null, Guid.NewGuid(), new DateOnly(2026, 7, 10)),
            AitAtLedgerEntry.CreateAddition(companyId, 2026, DutyComponent.Ait, 3000m, null, Guid.NewGuid(), new DateOnly(2026, 8, 10)),
            AitAtLedgerEntry.RecordAdjustment(companyId, 2026, DutyComponent.Ait, 2000m, "2026-07", null, new DateOnly(2026, 8, 15)),
            AitAtLedgerEntry.CreateAddition(companyId, 2026, DutyComponent.At, 1000m, null, Guid.NewGuid(), new DateOnly(2026, 7, 10)),
        };

        var mock = new Mock<IAitAtLedgerRepository>();
        mock.Setup(r => r.GetForCompanyFyAsync(companyId, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        var handler = new GetAitAtLedgerHandler(mock.Object);

        Result<AitAtLedgerResponse> result = await handler.HandleAsync(
            new GetAitAtLedgerQuery(companyId, 2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        AitAtLedgerResponse ledger = result.Value;
        ledger.AitAdditions.Should().Be(8000m);
        ledger.AitAdjustments.Should().Be(2000m);
        ledger.AitClosingBalance.Should().Be(6000m);
        ledger.AtAdditions.Should().Be(1000m);
        ledger.AtAdjustments.Should().Be(0m);
        ledger.AtClosingBalance.Should().Be(1000m);

        ledger.Entries.Should().NotBeNull();
        ledger.Entries!.Should().HaveCount(4);
    }

    [Fact]
    public async Task EmptyLedger_ZeroBalances()
    {
        var mock = new Mock<IAitAtLedgerRepository>();
        mock.Setup(r => r.GetForCompanyFyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AitAtLedgerEntry>)[]);
        var handler = new GetAitAtLedgerHandler(mock.Object);

        Result<AitAtLedgerResponse> result = await handler.HandleAsync(
            new GetAitAtLedgerQuery(Guid.NewGuid(), 2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AitClosingBalance.Should().Be(0m);
        result.Value.AtClosingBalance.Should().Be(0m);
        result.Value.Entries.Should().BeEmpty();
    }
}
