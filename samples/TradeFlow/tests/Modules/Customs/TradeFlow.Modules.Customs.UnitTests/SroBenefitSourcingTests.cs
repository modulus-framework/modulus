using FluentAssertions;
using Moq;
using Modulus.Core.Abstractions;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Application.Duty.Queries;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.UnitTests;

[Trait("Category", "Unit")]
public sealed class SroBenefitItemizationTests
{
    private static readonly DateOnly AssessmentDate = new(2026, 8, 15);

    private static DutyRateRow Rate(DutyComponent component, decimal rate)
        => new(Guid.NewGuid(), component, rate, null, null, new DateOnly(2026, 7, 1), null);

    private static DutyCalculationResult Calculate(
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates,
        params SroBenefitApplication[] benefits)
        => DutyCascadeCalculator.Calculate(
            quantity: 100m, unitPriceFcy: 10m, freightShareFcy: 0m, insuranceShareFcy: 0m,
            customsExchangeRate: 1m, landingChargePct: DutyCascadeCalculator.DefaultLandingChargePct,
            tariffValueBdt: null, rates, benefits);

    [Fact]
    public void ExemptBenefit_ItemizedOnComponentResult()
    {
        Guid benefitId = Guid.NewGuid();
        var rates = new Dictionary<DutyComponent, DutyRateRow> { [DutyComponent.Cd] = Rate(DutyComponent.Cd, 0.10m) };
        var benefit = new SroBenefitApplication(benefitId, "SRO-100-DA", SroBenefitType.Exempt, null, null);

        DutyCalculationResult calc = Calculate(rates, benefit);

        DutyComponentResult cd = calc.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.IsSroExempt.Should().BeTrue();
        cd.Amount.Should().Be(0m);
        cd.SroBenefitId.Should().Be(benefitId);
        cd.SroBenefitName.Should().Be("SRO-100-DA");
        calc.Tti.Should().Be(0m);
    }

    [Fact]
    public void OverrideBenefit_ItemizedWithReducedRate()
    {
        Guid benefitId = Guid.NewGuid();
        var rates = new Dictionary<DutyComponent, DutyRateRow> { [DutyComponent.Cd] = Rate(DutyComponent.Cd, 0.10m) };
        var benefit = new SroBenefitApplication(benefitId, "SRO-200-EX", SroBenefitType.RateOverride, 0.05m, null);

        DutyCalculationResult calc = Calculate(rates, benefit);

        DutyComponentResult cd = calc.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.Rate.Should().Be(0.05m);
        cd.IsSroOverridden.Should().BeTrue();
        cd.SroBenefitId.Should().Be(benefitId);
        cd.SroBenefitName.Should().Be("SRO-200-EX");
        cd.Amount.Should().Be(50.50m);
    }

    [Fact]
    public void BindingCapBenefit_Itemized()
    {
        Guid benefitId = Guid.NewGuid();
        var rates = new Dictionary<DutyComponent, DutyRateRow> { [DutyComponent.Cd] = Rate(DutyComponent.Cd, 0.10m) };
        var benefit = new SroBenefitApplication(benefitId, "SRO-300-CAP", SroBenefitType.Cap, null, 0.05m);

        DutyCalculationResult calc = Calculate(rates, benefit);

        DutyComponentResult cd = calc.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.IsSroCapped.Should().BeTrue();
        cd.Amount.Should().Be(50.50m);
        cd.SroBenefitId.Should().Be(benefitId);
        cd.SroBenefitName.Should().Be("SRO-300-CAP");
    }

    [Fact]
    public void NonBindingCapBenefit_NotItemized()
    {
        var rates = new Dictionary<DutyComponent, DutyRateRow> { [DutyComponent.Cd] = Rate(DutyComponent.Cd, 0.10m) };
        var benefit = new SroBenefitApplication(Guid.NewGuid(), "SRO-300-CAP", SroBenefitType.Cap, null, 0.20m);

        DutyCalculationResult calc = Calculate(rates, benefit);

        DutyComponentResult cd = calc.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.IsSroCapped.Should().BeFalse();
        cd.SroBenefitId.Should().BeNull();
    }

    [Fact]
    public void NoBenefits_NoItemization()
    {
        var rates = new Dictionary<DutyComponent, DutyRateRow> { [DutyComponent.Cd] = Rate(DutyComponent.Cd, 0.10m) };

        DutyCalculationResult calc = Calculate(rates);

        DutyComponentResult cd = calc.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.IsSroExempt.Should().BeFalse();
        cd.IsSroOverridden.Should().BeFalse();
        cd.IsSroCapped.Should().BeFalse();
        cd.SroBenefitId.Should().BeNull();
        cd.SroBenefitName.Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public sealed class ResolveSroBenefitsHandlerTests
{
    private static readonly DateOnly AsOfDate = new(2026, 8, 15);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ICurrentTenant Tenant()
    {
        var mock = new Mock<ICurrentTenant>();
        mock.Setup(t => t.TenantId).Returns((Guid?)TenantId);
        return mock.Object;
    }

    private static IDutyRateRepository RateRepo(string hsCode, params DutyRateRow[] rates)
    {
        var mock = new Mock<IDutyRateRepository>();
        mock.Setup(r => r.GetEffectiveRatesAsync(hsCode, AsOfDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates.ToDictionary(r => r.Component));
        return mock.Object;
    }

    private static ISroBenefitRepository SroRepo(params SroBenefit[] benefits)
    {
        var mock = new Mock<ISroBenefitRepository>();
        mock.Setup(r => r.GetActiveOnAsync(AsOfDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SroBenefit>)benefits);
        return mock.Object;
    }

    [Fact]
    public async Task ExemptBenefit_ZeroEffectiveRateAndItemized()
    {
        string hsCode = "8471.30.00";
        SroBenefit benefit = SroBenefit.Create("SRO-100-DA", "8471", SroBenefitType.Exempt, new DateOnly(2026, 7, 1));
        var handler = new ResolveSroBenefitsHandler(
            RateRepo(hsCode,
                new DutyRateRow(Guid.NewGuid(), DutyComponent.Cd, 0.10m, null, null, new DateOnly(2026, 7, 1), null),
                new DutyRateRow(Guid.NewGuid(), DutyComponent.Vat, 0.15m, null, null, new DateOnly(2026, 7, 1), null)),
            SroRepo(benefit),
            Tenant());

        Result<SroSourceResponse> result = await handler.HandleAsync(new ResolveSroBenefitsQuery(hsCode, AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SroSourceResponse source = result.Value;
        source.HsCode.Should().Be(hsCode);
        source.Components.Should().HaveCount(2);
        source.Components.Should().OnlyContain(c => c.EffectiveRate == 0m && c.Effect == "Exempt");
        source.AppliedBenefits.Should().ContainSingle();
        source.AppliedBenefits[0].BenefitId.Should().Be(benefit.Id);
        source.AppliedBenefits[0].Name.Should().Be("SRO-100-DA");
    }

    [Fact]
    public async Task OverrideBenefit_OverriddenEffect()
    {
        string hsCode = "8471.30.00";
        SroBenefit benefit = SroBenefit.Create("SRO-200-EX", "8471", SroBenefitType.RateOverride,
            new DateOnly(2026, 7, 1), overrideRate: 0.05m);
        var handler = new ResolveSroBenefitsHandler(
            RateRepo(hsCode,
                new DutyRateRow(Guid.NewGuid(), DutyComponent.Cd, 0.10m, null, null, new DateOnly(2026, 7, 1), null)),
            SroRepo(benefit),
            Tenant());

        Result<SroSourceResponse> result = await handler.HandleAsync(new ResolveSroBenefitsQuery(hsCode, AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SroComponentSourceResponse cd = result.Value.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.BaseRate.Should().Be(0.10m);
        cd.EffectiveRate.Should().Be(0.05m);
        cd.Effect.Should().Be("Overridden");
    }

    [Fact]
    public async Task NoApplicableBenefits_BaseRatesUnchanged()
    {
        string hsCode = "8471.30.00";
        SroBenefit benefit = SroBenefit.Create("SRO-OTHER", "9999", SroBenefitType.Exempt, new DateOnly(2026, 7, 1));
        var handler = new ResolveSroBenefitsHandler(
            RateRepo(hsCode,
                new DutyRateRow(Guid.NewGuid(), DutyComponent.Cd, 0.10m, null, null, new DateOnly(2026, 7, 1), null)),
            SroRepo(benefit),
            Tenant());

        Result<SroSourceResponse> result = await handler.HandleAsync(new ResolveSroBenefitsQuery(hsCode, AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SroComponentSourceResponse cd = result.Value.Components.Single(c => c.Component == DutyComponent.Cd);
        cd.EffectiveRate.Should().Be(cd.BaseRate);
        cd.Effect.Should().Be("None");
        result.Value.AppliedBenefits.Should().BeEmpty();
    }
}

[Trait("Category", "Unit")]
public sealed class BulkDutyLookupHandlerTests
{
    private static readonly DateOnly AsOfDate = new(2026, 8, 15);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ICurrentTenant Tenant()
    {
        var mock = new Mock<ICurrentTenant>();
        mock.Setup(t => t.TenantId).Returns((Guid?)TenantId);
        return mock.Object;
    }

    private static IDutyRateRepository RateRepo(
        IReadOnlyDictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>> ratesByHs)
    {
        var mock = new Mock<IDutyRateRepository>();
        mock.Setup(r => r.GetEffectiveRatesForAsync(It.IsAny<IReadOnlyList<string>>(), AsOfDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ratesByHs);
        return mock.Object;
    }

    private static ISroBenefitRepository SroRepo(params SroBenefit[] benefits)
    {
        var mock = new Mock<ISroBenefitRepository>();
        mock.Setup(r => r.GetActiveOnAsync(AsOfDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SroBenefit>)benefits);
        return mock.Object;
    }

    private static IReadOnlyDictionary<DutyComponent, DutyRateRow> RatesFor(string hsCode)
        => new Dictionary<DutyComponent, DutyRateRow>
        {
            [DutyComponent.Cd] = new(Guid.NewGuid(), DutyComponent.Cd, 0.10m, null, null, new DateOnly(2026, 7, 1), null),
            [DutyComponent.Vat] = new(Guid.NewGuid(), DutyComponent.Vat, 0.15m, null, null, new DateOnly(2026, 7, 1), null),
        };

    [Fact]
    public async Task BulkLookup_MultipleCodes_ResolvesEach()
    {
        string hs1 = "8471.30.00";
        string hs2 = "8517.13.00";
        SroBenefit benefit = SroBenefit.Create("SRO-100-DA", "8471", SroBenefitType.Exempt, new DateOnly(2026, 7, 1));
        var rates = new Dictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>>
        {
            [hs1] = RatesFor(hs1),
            [hs2] = RatesFor(hs2),
        };
        var handler = new BulkDutyLookupHandler(RateRepo(rates), SroRepo(benefit), Tenant());

        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await handler.HandleAsync(
            new BulkDutyLookupQuery([hs1, hs2], AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        BulkDutyLookupEntryResponse first = result.Value.Single(e => e.HsCode == hs1);
        first.RatesFound.Should().BeTrue();
        first.ComponentRates.Select(r => r.Component).Should().BeEquivalentTo([DutyComponent.Cd, DutyComponent.Vat]);
        first.SroBenefits.Should().ContainSingle().Which.BenefitId.Should().Be(benefit.Id);

        BulkDutyLookupEntryResponse second = result.Value.Single(e => e.HsCode == hs2);
        second.RatesFound.Should().BeTrue();
        second.SroBenefits.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkLookup_UnknownCode_NotFoundFlag()
    {
        var handler = new BulkDutyLookupHandler(RateRepo(new Dictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>>()), SroRepo(), Tenant());

        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await handler.HandleAsync(
            new BulkDutyLookupQuery(["0000.00.00"], AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        BulkDutyLookupEntryResponse entry = result.Value.Single();
        entry.RatesFound.Should().BeFalse();
        entry.ComponentRates.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkLookup_TenantConditionMismatch_ExcludesBenefit()
    {
        string hsCode = "8471.30.00";
        SroBenefit benefit = SroBenefit.Create("SRO-BONDED", "8471", SroBenefitType.Exempt,
            new DateOnly(2026, 7, 1), conditions: Guid.NewGuid().ToString());
        var rates = new Dictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>> { [hsCode] = RatesFor(hsCode) };
        var handler = new BulkDutyLookupHandler(RateRepo(rates), SroRepo(benefit), Tenant());

        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await handler.HandleAsync(
            new BulkDutyLookupQuery([hsCode], AsOfDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().SroBenefits.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkLookup_EmptyList_Validation()
    {
        var handler = new BulkDutyLookupHandler(RateRepo(new Dictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>>()), SroRepo(), Tenant());

        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await handler.HandleAsync(
            new BulkDutyLookupQuery([], AsOfDate), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DutyLookup.Empty");
    }

    [Fact]
    public async Task BulkLookup_OverCap_Validation()
    {
        var handler = new BulkDutyLookupHandler(RateRepo(new Dictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>>()), SroRepo(), Tenant());
        var codes = Enumerable.Range(0, BulkDutyLookupHandler.MaxHsCodes + 1).Select(i => $"{i:0000}.00.00").ToList();

        Result<IReadOnlyList<BulkDutyLookupEntryResponse>> result = await handler.HandleAsync(
            new BulkDutyLookupQuery(codes, AsOfDate), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DutyLookup.TooMany");
    }
}
