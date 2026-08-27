using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Catalog.Domain.Entities;
using ModulusSample.Modules.Catalog.Domain.Repositories;
using ModulusSample.Modules.Catalog.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Infrastructure.Repositories;

public sealed class EfProductRepository(CatalogDbContext context) : IProductRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PagedResult<Product>> ListAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var skip = (page - 1) * pageSize;

        var totalCount = await context.Products.CountAsync(ct);

        var products = await context.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Product>(products, totalCount, page, pageSize);
    }

    public async Task AddAsync(Product product, CancellationToken ct = default)
    {
        await context.Products.AddAsync(product, ct);
    }
}