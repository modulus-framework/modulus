using ProcureFlow.Modules.Vendors.Domain.Entities;

namespace ProcureFlow.Modules.Vendors.Domain.Repositories;

public interface IVendorRepository
{
    Task<Vendor?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Vendor>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Vendor>> GetFilteredAsync(
        Guid tenantId,
        VendorStatus? status = null,
        string? country = null,
        VendorType? vendorType = null,
        string? searchTerm = null,
        CancellationToken ct = default);
    Task<bool> ExistsByKeyAsync(Guid tenantId, string duplicateKey, CancellationToken ct = default);
    Task AddAsync(Vendor vendor, CancellationToken ct = default);
    Task UpdateAsync(Vendor vendor, CancellationToken ct = default);
}
