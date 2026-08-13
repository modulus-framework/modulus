using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Repositories;

public sealed class EfRequisitionRepository(PurchasingDbContext context) : IRequisitionRepository
{
    public async Task<PurchaseRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Requisitions.FindAsync([id], ct);
    }

    public async Task<PagedResult<PurchaseRequisition>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.Requisitions.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var requisitions = await query
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<PurchaseRequisition>(requisitions, totalCount, page, pageSize);
    }

    public async Task AddAsync(PurchaseRequisition requisition, CancellationToken ct = default)
    {
        await context.Requisitions.AddAsync(requisition, ct);
    }
}