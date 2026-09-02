using FluentAssertions;
using Moq;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Application.Duty.Queries;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.UnitTests;

[Trait("Category", "Unit")]
public sealed class BoeLineSroSavingsTests
{
    private static BoeLine NewLine()
        => new(Guid.NewGuid(), null, "8471.30.00", "Laptop", 10m, "PCS", 1000m, 120m, 0.01m);

    [Fact]
    public void RecordComputed_StoresSroSavings()
    {
        BoeLine line = NewLine();

        line.RecordComputed(5000m, [], 250.5m);

        line.ComputedTtiBdt.Should().Be(5000m);
        line.SroSavingsBdt.Should().Be(250.5m);
    }

    [Fact]
    public void RecordComputed_WithoutSavings_SroSavingsNull()
    {
        BoeLine line = NewLine();

        line.RecordComputed(5000m, []);

        line.ComputedTtiBdt.Should().Be(5000m);
        line.SroSavingsBdt.Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public sealed class GetDutyAnalysisHandlerTests
{
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    [Fact]
    public async Task Analysis_AggregatesByHsWithVarianceMixAndSavings()
    {
        Guid tenantId = Guid.NewGuid();
        BillOfEntry boe = BillOfEntry.Create(tenantId, Guid.NewGuid(), "BOE-100",
            new DateOnly(2026, 8, 10), "DHK", "AIN-1");

        BoeLine hs1 = new(Guid.NewGuid(), null, "8471.30.00", "Laptop", 10m, "PCS", 1000m, 120m, 0.01m);
        hs1.RecordComputed(1000m, [], 200m);
        hs1.Assess(1200m, new[] { new AssessedDutyLine("CD", 800m), new AssessedDutyLine("VAT", 400m) });
        boe.AddLine(hs1);

        BoeLine hs2 = new(Guid.NewGuid(), null, "8517.13.00", "Phone", 5m, "PCS", 500m, 120m, 0.01m);
        hs2.RecordComputed(3000m, []);
        hs2.Assess(3150m, new[] { new AssessedDutyLine("CD", 3150m) });
        boe.AddLine(hs2);

        BoeLine unassessed = new(Guid.NewGuid(), null, "8471.30.00", "Pending", 1m, "PCS", 100m, 120m, 0.01m);
        boe.AddLine(unassessed);

        var mock = new Mock<IBoeRepository>();
        mock.Setup(r => r.GetAssessedBetweenAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BillOfEntry>)new List<BillOfEntry> { boe });
        var handler = new GetDutyAnalysisHandler(mock.Object);

        Result<DutyAnalysisResponse> result = await handler.HandleAsync(new GetDutyAnalysisQuery(From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        DutyAnalysisResponse analysis = result.Value;

        analysis.LineCount.Should().Be(2);
        analysis.ComputedTtiBdt.Should().Be(4000m);
        analysis.AssessedTtiBdt.Should().Be(4350m);
        analysis.VarianceBdt.Should().Be(350m);
        analysis.UpliftPct.Should().Be(decimal.Round(350m / 4000m, 6));
        analysis.SroSavingsBdt.Should().Be(200m);

        analysis.ByHsCode.Should().HaveCount(2);

        DutyHsAnalysisResponse first = analysis.ByHsCode[0];
        first.HsCode.Should().Be("8471.30.00");
        first.LineCount.Should().Be(1);
        first.DeclaredAvBdt.Should().Be(120000m);
        first.AssessedTtiBdt.Should().Be(1200m);
        first.EffectiveDutyPct.Should().Be(decimal.Round(1200m / 120000m, 6));
        first.SroSavingsBdt.Should().Be(200m);
        first.ComponentMix.Select(m => m.Component).Should().ContainInOrder("CD", "VAT");
        first.ComponentMix.Single(m => m.Component == "CD").Amount.Should().Be(800m);

        DutyHsAnalysisResponse second = analysis.ByHsCode[1];
        second.HsCode.Should().Be("8517.13.00");
        second.SroSavingsBdt.Should().Be(0m);
        second.UpliftPct.Should().Be(decimal.Round(150m / 3000m, 6));
    }

    [Fact]
    public async Task Analysis_NoAssessedLines_ZeroTotals()
    {
        var mock = new Mock<IBoeRepository>();
        mock.Setup(r => r.GetAssessedBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BillOfEntry>)[]);
        var handler = new GetDutyAnalysisHandler(mock.Object);

        Result<DutyAnalysisResponse> result = await handler.HandleAsync(new GetDutyAnalysisQuery(From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LineCount.Should().Be(0);
        result.Value.ComputedTtiBdt.Should().Be(0m);
        result.Value.UpliftPct.Should().Be(0m);
        result.Value.ByHsCode.Should().BeEmpty();
    }
}
