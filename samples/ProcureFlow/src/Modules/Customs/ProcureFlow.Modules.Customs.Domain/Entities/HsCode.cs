using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Domain.Entities;

/// <summary>
/// Effective-dated BD 8-digit tariff line (BR-HS-01). No overlapping periods
/// for a single HS code — enforced by the store via a dated-validity check.
/// </summary>
public sealed class HsCode : AggregateRoot
{
    private HsCode() { }

    private HsCode(Guid id, string code, string description, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        Id = id;
        Code = code;
        Description = description;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    public static HsCode Create(string code, string description, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 4 or > 12)
            throw new ArgumentException("HS code must be 4–12 digits", nameof(code));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));
        if (effectiveTo.HasValue && effectiveTo < effectiveFrom)
            throw new ArgumentException("EffectiveTo must be >= EffectiveFrom");

        return new HsCode(Guid.NewGuid(), code.Trim(), description.Trim(), effectiveFrom, effectiveTo);
    }
}