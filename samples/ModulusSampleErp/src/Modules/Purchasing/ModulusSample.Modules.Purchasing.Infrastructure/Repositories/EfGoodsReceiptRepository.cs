using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Repositories;

public sealed class EfGoodsReceiptRepository(PurchasingDbContext context) : IGoodsReceiptRepository
{
    public async Task<GoodsReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Receipts.FindAsync([id], ct);
    }

    public async Task<PagedResult<GoodsReceipt>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.Receipts.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var receipts = await query
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<GoodsReceipt>(receipts, totalCount, page, pageSize);
    }

    public async Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default)
    {
        await context.Receipts.AddAsync(receipt, ct);
    }
}