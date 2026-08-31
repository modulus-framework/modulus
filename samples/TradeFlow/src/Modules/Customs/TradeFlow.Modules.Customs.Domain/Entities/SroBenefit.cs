using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Domain.Entities;

/// <summary>
/// SRO benefit registry entry (BR-DS-05). Benefits resolve by HS-code prefix +
/// tenant eligibility and are itemized on the duty breakdown.
/// </summary>
public sealed class SroBenefit : AggregateRoot
{
    private SroBenefit() { }

    private SroBenefit(Guid id, string name, string hsCodePrefix, SroBenefitType type, decimal? overrideRate,
        decimal? capPercent, string conditions, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        Id = id;
        Name = name;
        HsCodePrefix = hsCodePrefix;
        Type = type;
        OverrideRate = overrideRate;
        CapPercent = capPercent;
        Conditions = conditions;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public string Name { get; private set; } = null!;
    public string HsCodePrefix { get; private set; } = null!;
    public SroBenefitType Type { get; private set; }
    public decimal? OverrideRate { get; private set; }
    public decimal? CapPercent { get; private set; }
    public string Conditions { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    public static SroBenefit Create(string name, string hsCodePrefix, SroBenefitType type, DateOnly effectiveFrom,
        decimal? overrideRate = null, decimal? capPercent = null, string conditions = "", DateOnly? effectiveTo = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(hsCodePrefix))
            throw new ArgumentException("HS-code prefix is required", nameof(hsCodePrefix));
        if (type == SroBenefitType.RateOverride && (!overrideRate.HasValue || overrideRate < 0m))
            throw new ArgumentException("Rate override benefits require a non-negative override rate");
        if (type == SroBenefitType.Cap && (!capPercent.HasValue || capPercent < 0m || capPercent > 1m))
            throw new ArgumentException("Cap benefits require a cap percent in [0,1]");
        if (effectiveTo.HasValue && effectiveTo < effectiveFrom)
            throw new ArgumentException("EffectiveTo must be >= EffectiveFrom");

        return new SroBenefit(Guid.NewGuid(), name.Trim(), hsCodePrefix.Trim(), type, overrideRate, capPercent,
            conditions.Trim(), effectiveFrom, effectiveTo);
    }

    public bool IsEffectiveOn(DateOnly date) => date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    /// <summary>Whether this benefit applies to the given HS code and tenant.</summary>
    public bool AppliesTo(string hsCode, Guid tenantId)
        => hsCode.StartsWith(HsCodePrefix, StringComparison.Ordinal) &&
           (Conditions.Length == 0 || Conditions.Contains(tenantId.ToString(), StringComparison.OrdinalIgnoreCase));
}