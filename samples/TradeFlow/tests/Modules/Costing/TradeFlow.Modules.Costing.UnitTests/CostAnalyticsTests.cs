using FluentAssertions;
using Moq;
using Modulus.Core.Abstractions;
using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Modules.Costing.Application.Queries;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.UnitTests;

[Trait("Category", "Unit")]
public sealed class GetCostAnalyticsHandlerTests
{
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ICurrentTenant Tenant()
    {
        var mock = new Mock<ICurrentTenant>();
        mock.Setup(t => t.TenantId).Returns((Guid?)TenantId);
        return mock.Object;
    }

    private static ILandedCostSheetRepository Repo(params LandedCostSheet[] sheets)
    {
        var mock = new Mock<ILandedCostSheetRepository>();
        mock.Setup(r => r.GetFinalizedBetweenAsync(TenantId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LandedCostSheet>)sheets.ToList());
        return mock.Object;
    }

    private static LandedCostSheet FinalizedSheet(string sheetNumber)
    {
        LandedCostSheet sheet = LandedCostSheet.Create(TenantId, Guid.NewGuid(), sheetNumber, "BDT");
        sheet.AddLine(Guid.NewGuid(), goodsValueFcy: 10000m, goodsValueBdt: 1200000m, receivedQty: 100m,
            netWeightKg: 500m, grossWeightKg: 550m, volumeCbm: 3m, containerShare: 1m);
        return sheet;
    }

    private static CostElement Element(string name, decimal amountBdt, CostElementDriver driver, CostTreatment treatment)
        => new(Guid.NewGuid(), name, amountFcy: amountBdt / 120m, fxRate: 120m, amountBdt,
            driver, CostElementScope.File, treatment, "BoE", "BOE-1");

    [Fact]
    public async Task Analytics_DutyPortionExcludesAdvanceAssetAndRecoverable()
    {
        LandedCostSheet sheet = FinalizedSheet("LCS-001");
        sheet.AddElement(Element("Duty: Customs Duty (BOE-1)", 960000m, CostElementDriver.Direct, CostTreatment.LandedCost));
        sheet.AddElement(Element("Ocean Freight", 100000m, CostElementDriver.Value, CostTreatment.LandedCost));
        sheet.AddElement(Element("Duty: Advance Income Tax (BOE-1)", 50000m, CostElementDriver.Direct, CostTreatment.AdvanceAsset));
        sheet.AddElement(Element("VAT (Recoverable)", 30000m, CostElementDriver.Direct, CostTreatment.Recoverable));
        sheet.Allocate();
        sheet.SubmitForFinalization();

        Result<CostAnalyticsResponse> result = await new GetCostAnalyticsHandler(Repo(sheet), Tenant())
            .HandleAsync(new GetCostAnalyticsQuery(From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        CostSheetAnalyticsResponse analytics = result.Value.Sheets.Single();

        analytics.TotalLandedCostBdt.Should().Be(1200000m + 960000m + 100000m + 50000m + 30000m);
        analytics.DutyPortionBdt.Should().Be(960000m);
        analytics.DutyPctOfLanded.Should().Be(decimal.Round(960000m / analytics.TotalLandedCostBdt, 6));
        analytics.LandedCostPortionBdt.Should().Be(960000m + 100000m);
        analytics.AdvanceAssetPortionBdt.Should().Be(50000m);
        analytics.RecoverablePortionBdt.Should().Be(30000m);
        analytics.LineCount.Should().Be(1);
        analytics.AvgUnitCost.Should().Be(decimal.Round(analytics.TotalLandedCostBdt / 100m, 4));

        result.Value.Trend.Should().ContainSingle();
        CostTrendPointResponse point = result.Value.Trend[0];
        point.TotalLandedCostBdt.Should().Be(analytics.TotalLandedCostBdt);
        point.DutyPortionBdt.Should().Be(960000m);
        point.DutyPct.Should().Be(analytics.DutyPctOfLanded);
    }

    [Fact]
    public async Task Analytics_NoSheets_EmptyResult()
    {
        Result<CostAnalyticsResponse> result = await new GetCostAnalyticsHandler(Repo(), Tenant())
            .HandleAsync(new GetCostAnalyticsQuery(From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sheets.Should().BeEmpty();
        result.Value.Trend.Should().BeEmpty();
    }
}

[Trait("Category", "Unit")]
public sealed class GetRevaluationHistoryHandlerTests
{
    [Fact]
    public async Task History_MapsRunsWithTotals()
    {
        Guid tenantId = Guid.NewGuid();
        RevaluationRun run = RevaluationRun.Start(tenantId, new DateOnly(2026, 7, 31));
        run.AddVariance(Guid.NewGuid(), "LCS-001", Guid.NewGuid(), "Ocean Freight", "USD",
            1000m, 110m, 110000m, 120m, 120000m);
        run.Complete(3);

        var mock = new Mock<IRevaluationRunRepository>();
        mock.Setup(r => r.GetByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RevaluationRun>)new List<RevaluationRun> { run });

        Result<IReadOnlyList<RevaluationRunResponse>> result = await new GetRevaluationHistoryHandler(mock.Object, TenantStub(tenantId))
            .HandleAsync(new GetRevaluationHistoryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        RevaluationRunResponse response = result.Value.Single();
        response.RunId.Should().Be(run.Id);
        response.PeriodEnd.Should().Be(new DateOnly(2026, 7, 31));
        response.Status.Should().Be(RevaluationRunStatus.Completed);
        response.SheetsScanned.Should().Be(3);
        response.TotalOriginalValueBdt.Should().Be(110000m);
        response.TotalRevaluedValueBdt.Should().Be(120000m);
        response.TotalFxGainLossBdt.Should().Be(10000m);
        response.VarianceCount.Should().Be(1);
    }

    private static ICurrentTenant TenantStub(Guid tenantId)
    {
        var mock = new Mock<ICurrentTenant>();
        mock.Setup(t => t.TenantId).Returns((Guid?)tenantId);
        return mock.Object;
    }
}
