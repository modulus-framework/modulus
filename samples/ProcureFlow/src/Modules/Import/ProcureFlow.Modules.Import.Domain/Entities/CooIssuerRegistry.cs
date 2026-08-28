using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Domain.Entities;

/// <summary>
/// COO issuance registry — tracks which issuers/embassies are valid per country.
/// </summary>
public sealed class CooIssuerRegistry : AggregateRoot
{
    private CooIssuerRegistry() { }

    private CooIssuerRegistry(Guid id, Guid tenantId, string country, string issuerName,
        string? licenseNo, DateOnly validFrom, DateOnly? validTo)
    {
        Id = id;
        TenantId = tenantId;
        Country = country;
        IssuerName = issuerName;
        LicenseNo = licenseNo;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public Guid TenantId { get; private set; }
    public string Country { get; private set; } = null!;
    public string IssuerName { get; private set; } = null!;
    public string? LicenseNo { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }

    public static CooIssuerRegistry Create(Guid tenantId, string country, string issuerName,
        string? licenseNo, DateOnly validFrom, DateOnly? validTo)
    {
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country is required", nameof(country));
        if (string.IsNullOrWhiteSpace(issuerName))
            throw new ArgumentException("Issuer name is required", nameof(issuerName));
        if (validTo.HasValue && validTo < validFrom)
            throw new ArgumentException("ValidTo must be >= ValidFrom", nameof(validTo));

        return new CooIssuerRegistry(Guid.NewGuid(), tenantId, country.Trim(),
            issuerName.Trim(), licenseNo?.Trim(), validFrom, validTo);
    }

    public bool IsActiveOn(DateOnly date) =>
        ValidFrom <= date && (!ValidTo.HasValue || ValidTo >= date);
}