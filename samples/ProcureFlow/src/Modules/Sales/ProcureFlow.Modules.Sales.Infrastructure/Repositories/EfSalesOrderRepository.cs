using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Sales.Domain.Entities;
using ModulusSample.Modules.Sales.Domain.Repositories;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Infrastructure.Repositories;

public sealed class EfSalesOrderRepository(SalesDbContext context) : ISalesOrderRepository
{
    public async Task<SalesOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<PagedResult<SalesOrder>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var skip = (page - 1) * pageSize;

        var totalCount = await context.Orders.CountAsync(ct);

        var orders = await context.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<SalesOrder>(orders, totalCount, page, pageSize);
    }

    public async Task AddAsync(SalesOrder order, CancellationToken ct = default)
    {
        await context.Orders.AddAsync(order, ct);
    }
}