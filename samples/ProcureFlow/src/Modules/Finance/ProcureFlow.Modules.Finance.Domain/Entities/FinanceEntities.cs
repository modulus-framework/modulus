namespace ProcureFlow.Modules.Finance.Domain.Entities;

/// <summary>FX rate with effective dating and source tagging (BR-FIN-06).</summary>
public sealed class FxRate
{
    private FxRate() { }

    public FxRate(Guid id, Guid tenantId, DateOnly effectiveDate, string fromCurrency, string toCurrency,
        decimal rate, FxSource source, string? sourceReference, DateTime uploadedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        EffectiveDate = effectiveDate;
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Rate = rate;
        Source = source;
        SourceReference = sourceReference;
        UploadedAtUtc = uploadedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string FromCurrency { get; private set; } = null!;
    public string ToCurrency { get; private set; } = null!;
    public decimal Rate { get; private set; }
    public FxSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }
}

/// <summary>Cost center for expense allocation (BR-FIN-03).</summary>
public sealed class CostCenter
{
    private CostCenter() { }

    public CostCenter(Guid id, Guid tenantId, string code, string name, Guid? parentId, bool isActive)
    {
        Id = id;
        TenantId = tenantId;
        Code = code;
        Name = name;
        ParentId = parentId;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }
}