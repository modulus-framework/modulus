using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Repositories;

public sealed class EfPurchaseOrderRepository(PurchasingDbContext context) : IPurchaseOrderRepository
{
    public async Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Orders.FindAsync([id], ct);
    }

    public async Task<PagedResult<PurchaseOrder>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.Orders.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var orders = await query
            .OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<PurchaseOrder>(orders, totalCount, page, pageSize);
    }

    public async Task AddAsync(PurchaseOrder order, CancellationToken ct = default)
    {
        await context.Orders.AddAsync(order, ct);
    }
}