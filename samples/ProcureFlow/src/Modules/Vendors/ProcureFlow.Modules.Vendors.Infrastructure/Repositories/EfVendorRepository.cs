using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using ProcureFlow.Modules.Vendors.Infrastructure.Database;

namespace ProcureFlow.Modules.Vendors.Infrastructure.Repositories;

public sealed class EfVendorRepository(VendorsDbContext context) : IVendorRepository
{
    public async Task<Vendor?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Vendors
            .AsSplitQuery()
            .Include(v => v.Qualifications)
            .Include(v => v.BankAccounts)
            .Include(v => v.Scorecards)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<IReadOnlyList<Vendor>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.Vendors
            .Where(v => v.TenantId == tenantId)
            .OrderBy(v => v.Name)
            .AsSplitQuery()
            .Include(v => v.Qualifications)
            .Include(v => v.BankAccounts)
            .Include(v => v.Scorecards)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Vendor>> GetFilteredAsync(
        Guid tenantId,
        VendorStatus? status = null,
        string? country = null,
        VendorType? vendorType = null,
        string? searchTerm = null,
        CancellationToken ct = default)
    {
        var query = context.Vendors.Where(v => v.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(v => v.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(v => v.Country == country);

        if (vendorType.HasValue)
            query = query.Where(v => v.VendorType == vendorType.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query = query.Where(v =>
                v.Name.Contains(searchTerm) ||
                v.LegalName.Contains(searchTerm) ||
                (v.Tin != null && v.Tin.Contains(searchTerm)) ||
                (v.Bin != null && v.Bin.Contains(searchTerm)));

        return await query
            .OrderBy(v => v.Name)
            .AsSplitQuery()
            .Include(v => v.Qualifications)
            .Include(v => v.BankAccounts)
            .Include(v => v.Scorecards)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByKeyAsync(Guid tenantId, string duplicateKey, CancellationToken ct = default)
    {
        // BR-VEN-02: duplicate if any existing vendor shares the TIN, the BIN,
        // or the normalized (name, country) pair. The raw fields are compared so
        // EF can translate the query (DuplicateKey is a computed property).
        string name = duplicateKey;
        string? country = null;

        // The key format is "tin:X|bin:Y|name:Z|country:W" — extract parts.
        string? tin = null;
        string? bin = null;
        foreach (string part in duplicateKey.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("tin:", StringComparison.Ordinal))
                tin = part[4..];
            else if (part.StartsWith("bin:", StringComparison.Ordinal))
                bin = part[4..];
            else if (part.StartsWith("name:", StringComparison.Ordinal))
                name = part[5..];
            else if (part.StartsWith("country:", StringComparison.Ordinal))
                country = part[8..];
        }

        return await context.Vendors.AnyAsync(v =>
            v.TenantId == tenantId &&
            ((tin != null && v.Tin != null && v.Tin == tin) ||
             (bin != null && v.Bin != null && v.Bin == bin) ||
             (name != null && country != null && v.Name.Trim().ToUpperInvariant() == name && v.Country.Trim().ToUpperInvariant() == country)), ct);
    }

    public async Task AddAsync(Vendor vendor, CancellationToken ct = default)
    {
        await context.Vendors.AddAsync(vendor, ct);
    }

    public async Task UpdateAsync(Vendor vendor, CancellationToken ct = default)
    {
        context.Vendors.Update(vendor);
        await Task.CompletedTask;
    }
}