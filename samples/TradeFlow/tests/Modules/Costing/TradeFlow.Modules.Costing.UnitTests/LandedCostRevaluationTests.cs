using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradeFlow.Modules.Costing.Application.BackgroundJobs;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Events;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Modules.Costing.Domain.Services;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.UnitTests;

[Trait("Category", "Unit")]
public sealed class RevaluationRunTests
{
    [Fact]
    public void Start_CreatesRunInProgress()
    {
        Guid tenantId = Guid.NewGuid();

        RevaluationRun run = RevaluationRun.Start(tenantId, new DateOnly(2026, 8, 31));

        run.TenantId.Should().Be(tenantId);
        run.PeriodEnd.Should().Be(new DateOnly(2026, 8, 31));
        run.Status.Should().Be(RevaluationRunStatus.InProgress);
        run.Variances.Should().BeEmpty();
        run.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Start_EmptyTenantId_Throws()
    {
        Action act = () => RevaluationRun.Start(Guid.Empty, new DateOnly(2026, 8, 31));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddVariance_ComputesGainLoss()
    {
        RevaluationRun run = RevaluationRun.Start(Guid.NewGuid(), new DateOnly(2026, 8, 31));

        run.AddVariance(Guid.NewGuid(), "LCS-1", Guid.NewGuid(), "Freight (BL-1)", "usd",
            1000m, 120m, 120000m, 125m, 125000m);

        run.Variances.Should().HaveCount(1);
        RevaluationVariance variance = run.Variances[0];
        variance.Currency.Should().Be("USD");
        variance.FxGainLossBdt.Should().Be(5000m);
    }

    [Fact]
    public void AddVariance_AfterComplete_Throws()
    {
        RevaluationRun run = RevaluationRun.Start(Guid.NewGuid(), new DateOnly(2026, 8, 31));
        run.Complete(0);

        Action act = () => run.AddVariance(Guid.NewGuid(), "LCS-1", Guid.NewGuid(), "Freight", "USD",
            1m, 1m, 1m, 1m, 1m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddVariance_NonPositiveRate_Throws()
    {
        RevaluationRun run = RevaluationRun.Start(Guid.NewGuid(), new DateOnly(2026, 8, 31));

        Action act = () => run.AddVariance(Guid.NewGuid(), "LCS-1", Guid.NewGuid(), "Freight", "USD",
            1m, 1m, 1m, 0m, 1m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_ComputesTotalsAndRaisesEvent()
    {
        RevaluationRun run = RevaluationRun.Start(Guid.NewGuid(), new DateOnly(2026, 8, 31));
        run.AddVariance(Guid.NewGuid(), "LCS-1", Guid.NewGuid(), "Freight", "USD",
            1000m, 120m, 120000m, 125m, 125000m);
        run.AddVariance(Guid.NewGuid(), "LCS-2", Guid.NewGuid(), "Insurance", "EUR",
            500m, 130m, 65000m, 132m, 66000m);

        run.Complete(7);

        run.Status.Should().Be(RevaluationRunStatus.Completed);
        run.SheetsScanned.Should().Be(7);
        run.CompletedAtUtc.Should().NotBeNull();
        run.TotalOriginalValueBdt.Should().Be(185000m);
        run.TotalRevaluedValueBdt.Should().Be(191000m);
        run.TotalFxGainLossBdt.Should().Be(6000m);

        run.DomainEvents.Should().HaveCount(1);
        run.DomainEvents[0].Should().BeOfType<LandedCostRevaluedDomainEvent>();
        LandedCostRevaluedDomainEvent @event = (LandedCostRevaluedDomainEvent)run.DomainEvents[0];
        @event.RunId.Should().Be(run.Id);
        @event.SheetsScanned.Should().Be(7);
        @event.VarianceCount.Should().Be(2);
        @event.TotalFxGainLossBdt.Should().Be(6000m);
    }

    [Fact]
    public void Complete_Twice_Throws()
    {
        RevaluationRun run = RevaluationRun.Start(Guid.NewGuid(), new DateOnly(2026, 8, 31));
        run.Complete(0);

        Action act = () => run.Complete(0);

        act.Should().Throw<InvalidOperationException>();
    }
}

[Trait("Category", "Unit")]
public sealed class LandedCostRevaluationServiceTests
{
    private static LandedCostSheet FinalizedSheet(string number)
    {
        LandedCostSheet sheet = LandedCostSheet.Create(Guid.NewGuid(), Guid.NewGuid(), number, "USD");
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 1000m, 120m, 120000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1",
            currency: "USD"));
        sheet.SubmitForFinalization();
        return sheet;
    }

    private static ILandedCostSheetRepository Repo(params LandedCostSheet[] sheets)
    {
        var mock = new Mock<ILandedCostSheetRepository>();
        mock.Setup(r => r.GetFinalizedByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sheets);
        return mock.Object;
    }

    [Fact]
    public async Task Revaluates_FxElement_AtNewRate()
    {
        var service = new LandedCostRevaluationService(Repo(FinalizedSheet("LCS-1")));
        var rates = new Dictionary<string, decimal> { ["USD"] = 125m };

        RevaluationRun run = await service.RevaluatePeriodAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 31), rates);

        run.Variances.Should().HaveCount(1);
        run.Variances[0].NewAmountBdt.Should().Be(125000m);
        run.Variances[0].FxGainLossBdt.Should().Be(5000m);
        run.TotalFxGainLossBdt.Should().Be(5000m);
        run.Status.Should().Be(RevaluationRunStatus.Completed);
    }

    [Fact]
    public async Task Skips_BdtDenominated_AndUnknownCurrency_Elements()
    {
        LandedCostSheet sheet = LandedCostSheet.Create(Guid.NewGuid(), Guid.NewGuid(), "LCS-1", "USD");
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Duty CD", 0m, 1m, 50000m,
            CostElementDriver.Direct, CostElementScope.File, CostTreatment.LandedCost, "BoE", "B-1",
            currency: null));
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Port charges", 0m, 1m, 8000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Receipt", "P-1",
            currency: "BDT"));
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 1000m, 120m, 120000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1",
            currency: "JPY"));
        sheet.SubmitForFinalization();
        var service = new LandedCostRevaluationService(Repo(sheet));
        var rates = new Dictionary<string, decimal> { ["USD"] = 125m };

        RevaluationRun run = await service.RevaluatePeriodAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 31), rates);

        run.Variances.Should().BeEmpty();
        run.SheetsScanned.Should().Be(1);
        run.TotalFxGainLossBdt.Should().Be(0m);
    }

    [Fact]
    public async Task Skips_Immaterial_Variances()
    {
        LandedCostSheet sheet = LandedCostSheet.Create(Guid.NewGuid(), Guid.NewGuid(), "LCS-1", "USD");
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 1000m, 120m, 120000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1",
            currency: "USD"));
        sheet.SubmitForFinalization();
        var service = new LandedCostRevaluationService(Repo(sheet));
        var rates = new Dictionary<string, decimal> { ["USD"] = 120.000001m };

        RevaluationRun run = await service.RevaluatePeriodAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 31), rates);

        run.Variances.Should().BeEmpty();
    }

    [Fact]
    public async Task NoSheets_CompletesEmptyRun()
    {
        var service = new LandedCostRevaluationService(Repo());

        RevaluationRun run = await service.RevaluatePeriodAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 31), new Dictionary<string, decimal>());

        run.SheetsScanned.Should().Be(0);
        run.Variances.Should().BeEmpty();
        run.Status.Should().Be(RevaluationRunStatus.Completed);
    }
}

[Trait("Category", "Unit")]
public sealed class RunPeriodicRevaluationHandlerTests
{
    [Fact]
    public async Task Persists_CompletedRun_AndReturnsSuccess()
    {
        var sheet = LandedCostSheet.Create(Guid.NewGuid(), Guid.NewGuid(), "LCS-1", "USD");
        sheet.AddElement(new CostElement(Guid.NewGuid(), "Freight", 1000m, 120m, 120000m,
            CostElementDriver.Value, CostElementScope.File, CostTreatment.LandedCost, "Bill", "FR-1",
            currency: "USD"));
        sheet.SubmitForFinalization();

        var sheetsRepo = new Mock<ILandedCostSheetRepository>();
        sheetsRepo.Setup(r => r.GetFinalizedByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LandedCostSheet>)[sheet]);

        var runsRepo = new Mock<IRevaluationRunRepository>();
        var unitOfWork = new Mock<TradeFlow.Modules.Costing.Application.IUnitOfWork>();
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RunPeriodicRevaluationHandler(
            new LandedCostRevaluationService(sheetsRepo.Object),
            runsRepo.Object,
            unitOfWork.Object,
            NullLogger<RunPeriodicRevaluationHandler>.Instance);

        var command = new RunPeriodicRevaluationCommand(
            Guid.NewGuid(), new DateOnly(2026, 8, 31),
            new Dictionary<string, decimal> { ["USD"] = 125m });

        Result<RevaluationRun> result = await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Variances.Should().HaveCount(1);
        runsRepo.Verify(r => r.AddAsync(It.IsAny<RevaluationRun>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
