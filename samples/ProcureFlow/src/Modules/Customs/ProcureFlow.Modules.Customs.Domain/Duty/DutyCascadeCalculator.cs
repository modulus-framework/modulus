using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Domain.Duty;

/// <summary>Duty/tax components in the BD cascade (§23.1).</summary>
public enum DutyComponent
{
    Cd = 1,
    Rd = 2,
    Sd = 3,
    Vat = 4,
    Ait = 5,
    At = 6,
}

/// <summary>Source of a duty rate row (BR-DS-01).</summary>
public enum DutyRateSource
{
    FinanceAct = 1,
    Sro = 2,
    Manual = 3,
}

/// <summary>SRO benefit kind (BR-DS-05).</summary>
public enum SroBenefitType
{
    Exempt = 1,
    RateOverride = 2,
    Cap = 3,
}

/// <summary>Effective-dated duty rate for one component of one HS code (BR-DS-01/02).</summary>
public sealed record DutyRateRow(
    Guid RateRowId,
    DutyComponent Component,
    decimal Rate,
    decimal? SpecificRate,
    string? Uom,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>An SRO benefit applicable to a line (BR-DS-05).</summary>
public sealed record SroBenefitApplication(
    Guid BenefitId,
    string Name,
    SroBenefitType Type,
    decimal? OverrideRate,
    decimal? CapPercent);

/// <summary>One computed duty component for a line — the reproducible artifact (BR-DS-04).</summary>
public sealed record DutyComponentResult(
    DutyComponent Component,
    Guid RateRowId,
    decimal Rate,
    string RateDescription,
    decimal BaseAmount,
    decimal Amount,
    bool IsSroExempt,
    bool IsSroOverridden,
    bool IsSroCapped,
    bool IsSpecific);

/// <summary>Full cascade output for one BoE line (BR-DS-04, §23.1).</summary>
public sealed record DutyCalculationResult(
    decimal CifFcy,
    decimal DeclaredAvBdt,
    decimal AvEffective,
    bool UsedTariffValue,
    IReadOnlyList<DutyComponentResult> Components,
    decimal Tti)
{
    public decimal GetComponentAmount(DutyComponent component)
        => Components.FirstOrDefault(c => c.Component == component)?.Amount ?? 0m;
}

/// <summary>
/// Deterministic duty cascade calculator (BRS §23.1). Pure — identical inputs
/// always yield identical outputs (BR-AI-07). Invariants (BR-AI-08):
/// <list type="bullet">
/// <item>rate↑ ⇒ TTI↑ (monotone — every component is non-decreasing in its rate)</item>
/// <item>TTI = Σ component amounts exactly</item>
/// </list>
/// </summary>
public static class DutyCascadeCalculator
{
    public const decimal DefaultLandingChargePct = 0.01m;

    /// <summary>
    /// Computes the full duty cascade for one BoE line. Each component output
    /// carries the rate-row id used so the calculation is reproducible for any
    /// historical consignment (BR-DS-04).
    /// </summary>
    public static DutyCalculationResult Calculate(
        decimal quantity,
        decimal unitPriceFcy,
        decimal freightShareFcy,
        decimal insuranceShareFcy,
        decimal customsExchangeRate,
        decimal landingChargePct,
        decimal? tariffValueBdt,
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates,
        IReadOnlyList<SroBenefitApplication> sroBenefits)
    {
        if (customsExchangeRate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(customsExchangeRate), "Customs FX rate must be positive");

        decimal cifFcy = (quantity * unitPriceFcy) + freightShareFcy + insuranceShareFcy;
        decimal declaredAvBdt = cifFcy * customsExchangeRate * (1m + landingChargePct);
        decimal avEffective = Math.Max(declaredAvBdt, tariffValueBdt ?? 0m);
        bool usedTariffValue = avEffective > declaredAvBdt;

        var components = new List<DutyComponentResult>();

        // CD/RD/AIT are AV-based; SD/VAT/AT stack on the running taxable base.
        decimal baseCd = avEffective;
        decimal cd = ComputeComponent(DutyComponent.Cd, baseCd, quantity, rates, sroBenefits, components);
        decimal rd = ComputeComponent(DutyComponent.Rd, baseCd, quantity, rates, sroBenefits, components);

        decimal baseSd = avEffective + cd + rd;
        decimal sd = ComputeComponent(DutyComponent.Sd, baseSd, quantity, rates, sroBenefits, components);

        decimal baseVatAt = avEffective + cd + rd + sd;
        decimal vat = ComputeComponent(DutyComponent.Vat, baseVatAt, quantity, rates, sroBenefits, components);
        decimal at = ComputeComponent(DutyComponent.At, baseVatAt, quantity, rates, sroBenefits, components);

        decimal ait = ComputeComponent(DutyComponent.Ait, avEffective, quantity, rates, sroBenefits, components);

        decimal tti = components.Sum(c => c.Amount);
        return new DutyCalculationResult(cifFcy, declaredAvBdt, avEffective, usedTariffValue, components, tti);
    }

    private static decimal ComputeComponent(
        DutyComponent component,
        decimal baseAmount,
        decimal quantity,
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates,
        IReadOnlyList<SroBenefitApplication> sroBenefits,
        IList<DutyComponentResult> results)
    {
        if (!rates.TryGetValue(component, out DutyRateRow? rate))
            return 0m;

        SroBenefitApplication? benefit = sroBenefits
            .FirstOrDefault(b => b.Type == SroBenefitType.Exempt || b.Type == SroBenefitType.Cap);

        if (benefit is { Type: SroBenefitType.Exempt })
        {
            results.Add(new DutyComponentResult(component, rate.RateRowId, rate.Rate, "0 (SRO exempt)", baseAmount, 0m, true, false, false, false));
            return 0m;
        }

        decimal rateUsed = rate.Rate;
        bool overridden = false;
        SroBenefitApplication? overrideBenefit = sroBenefits.FirstOrDefault(b => b.Type == SroBenefitType.RateOverride && b.OverrideRate.HasValue);
        if (overrideBenefit is not null)
        {
            rateUsed = overrideBenefit.OverrideRate!.Value;
            overridden = true;
        }

        decimal adValorem = Round2(baseAmount * rateUsed);

        decimal amount = adValorem;
        bool isSpecific = false;
        if (rate.SpecificRate.HasValue && quantity > 0m)
        {
            decimal specific = Round2(quantity * rate.SpecificRate.Value);
            if (specific > amount)
            {
                amount = specific;
                isSpecific = true;
            }
        }

        bool capped = false;
        SroBenefitApplication? capBenefit = sroBenefits.FirstOrDefault(b => b.Type == SroBenefitType.Cap && b.CapPercent.HasValue);
        if (capBenefit is not null)
        {
            decimal cap = Round2(baseAmount * capBenefit.CapPercent!.Value);
            if (amount > cap)
            {
                amount = cap;
                capped = true;
            }
        }

        results.Add(new DutyComponentResult(component, rate.RateRowId, rateUsed, rateUsed.ToString("P2"), baseAmount, amount, false, overridden, capped, isSpecific));
        return amount;
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);
}