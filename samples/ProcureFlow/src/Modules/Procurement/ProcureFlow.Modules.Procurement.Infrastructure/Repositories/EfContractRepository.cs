using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Modules.Procurement.Domain.Repositories;
using ProcureFlow.Modules.Procurement.Infrastructure.Database;

namespace ProcureFlow.Modules.Procurement.Infrastructure.Repositories;

public sealed class EfContractRepository(ProcurementDbContext context) : IContractRepository
{
    public async Task<Contract?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Contracts
            .Include(c => c.Lines)
            .Include(c => c.Documents)
            .Include(c => c.Milestones)
            .Include(c => c.Revisions)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<Contract>> GetAllAsync(
        Guid tenantId, ContractStatus? status, Guid? vendorId, CancellationToken ct = default)
    {
        IQueryable<Contract> query = context.Contracts
            .Where(c => c.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);
        if (vendorId.HasValue)
            query = query.Where(c => c.VendorId == vendorId.Value);

        return await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Contract>> GetExpiringAsync(Guid tenantId, int withinDays, CancellationToken ct = default)
    {
        DateOnly cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(withinDays));
        return await context.Contracts
            .Where(c => c.TenantId == tenantId &&
                c.Status == ContractStatus.Active &&
                c.EndDate <= cutoff)
            .OrderBy(c => c.EndDate)
            .ToListAsync(ct);
    }

    public async Task<Contract?> GetActiveByVendorAndItemAsync(Guid vendorId, Guid itemId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Contracts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.VendorId == vendorId &&
                c.Status == ContractStatus.Active &&
                c.Lines.Any(l => l.ItemId == itemId), ct);
    }

    public async Task AddAsync(Contract contract, CancellationToken ct = default)
    {
        await context.Contracts.AddAsync(contract, ct);
    }

    public async Task UpdateAsync(Contract contract, CancellationToken ct = default)
    {
        context.Contracts.Update(contract);
        await Task.CompletedTask;
    }
}
