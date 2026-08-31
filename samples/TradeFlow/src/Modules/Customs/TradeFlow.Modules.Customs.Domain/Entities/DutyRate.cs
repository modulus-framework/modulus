using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Domain.Entities;

public enum DutyRateStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// Effective-dated duty rate row keyed (hs_code, component, effective_from)
/// with maker-checker approval (BR-DS-01/02). Overlapping periods are
/// prevented by the store (DB exclusion constraint).
/// </summary>
public sealed class DutyRate : AggregateRoot
{
    private DutyRate() { }

    private DutyRate(Guid id, string hsCode, DutyComponent component, decimal rate, decimal? specificRate,
        string? uom, DateOnly effectiveFrom, DateOnly? effectiveTo, DutyRateSource source, string? refDoc,
        string maker)
    {
        Id = id;
        HsCode = hsCode;
        Component = component;
        Rate = rate;
        SpecificRate = specificRate;
        Uom = uom;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Source = source;
        RefDoc = refDoc;
        Maker = maker;
        Status = DutyRateStatus.Pending;
    }

    public string HsCode { get; private set; } = null!;
    public DutyComponent Component { get; private set; }
    public decimal Rate { get; private set; }
    public decimal? SpecificRate { get; private set; }
    public string? Uom { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public DutyRateSource Source { get; private set; }
    public string? RefDoc { get; private set; }
    public string Maker { get; private set; } = null!;
    public string? Checker { get; private set; }
    public DutyRateStatus Status { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public static DutyRate Create(string hsCode, DutyComponent component, decimal rate, DateOnly effectiveFrom,
        DateOnly? effectiveTo, DutyRateSource source, string maker, decimal? specificRate = null,
        string? uom = null, string? refDoc = null)
    {
        if (string.IsNullOrWhiteSpace(hsCode))
            throw new ArgumentException("HS code is required", nameof(hsCode));
        if (rate < 0m)
            throw new ArgumentException("Rate cannot be negative", nameof(rate));
        if (specificRate.HasValue && specificRate < 0m)
            throw new ArgumentException("Specific rate cannot be negative", nameof(specificRate));
        if (effectiveTo.HasValue && effectiveTo < effectiveFrom)
            throw new ArgumentException("EffectiveTo must be >= EffectiveFrom");
        if (string.IsNullOrWhiteSpace(maker))
            throw new ArgumentException("Maker is required", nameof(maker));

        return new DutyRate(Guid.NewGuid(), hsCode.Trim(), component, rate, specificRate, uom, effectiveFrom,
            effectiveTo, source, refDoc, maker.Trim());
    }

    public void Approve(string checker)
    {
        if (Status != DutyRateStatus.Pending)
            throw new InvalidOperationException($"Only pending rates can be approved (status {Status})");
        if (string.IsNullOrWhiteSpace(checker))
            throw new ArgumentException("Checker is required");

        Status = DutyRateStatus.Approved;
        Checker = checker.Trim();
        ApprovedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string checker, string? reason = null)
    {
        if (Status != DutyRateStatus.Pending)
            throw new InvalidOperationException($"Only pending rates can be rejected (status {Status})");

        Status = DutyRateStatus.Rejected;
        Checker = checker.Trim();
    }

    /// <summary>Whether this rate is effective on the given date (BR-DS-01).</summary>
    public bool IsEffectiveOn(DateOnly date) => date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);
}