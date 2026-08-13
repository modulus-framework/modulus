using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Inventory.Domain.Entities;
using ModulusSample.Modules.Inventory.Domain.Repositories;
using ModulusSample.Modules.Inventory.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Infrastructure.Repositories;

public sealed class EfWarehouseRepository(InventoryDbContext context) : IWarehouseRepository
{
    public async Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<PagedResult<Warehouse>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var skip = (page - 1) * pageSize;

        var totalCount = await context.Warehouses.CountAsync(ct);

        var warehouses = await context.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Warehouse>(warehouses, totalCount, page, pageSize);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken ct = default)
    {
        await context.Warehouses.AddAsync(warehouse, ct);
    }
}