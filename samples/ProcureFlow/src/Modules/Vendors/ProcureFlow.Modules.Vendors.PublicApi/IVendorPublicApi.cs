namespace ProcureFlow.Modules.Vendors.PublicApi;

/// <summary>
/// Read-side synchronous contract into the Vendors module (BRS §5.3). Lets other
/// modules (e.g. Procurement) validate a vendor at PO time without referencing
/// the Vendors module directly. Implemented in Vendors.Infrastructure/PublicApi.
/// </summary>
public interface IVendorPublicApi
{
    /// <summary>BR-VEN-08: vendor must be Active and not blacklisted to transact.</summary>
    Task<bool> IsVendorEligibleAsync(Guid vendorId, CancellationToken ct = default);

    /// <summary>BR-VEN-05: vendor holds an unexpired qualification for the category.</summary>
    Task<bool> IsQualifiedForCategoryAsync(Guid vendorId, string category, CancellationToken ct = default);
}
