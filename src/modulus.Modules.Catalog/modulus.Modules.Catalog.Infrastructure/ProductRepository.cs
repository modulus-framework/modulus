using Microsoft.EntityFrameworkCore;
using Modulus.Data.Abstractions;
using modulus.Modules.Catalog.Domain;

namespace modulus.Modules.Catalog.Infrastructure;

public sealed class ProductRepository(DbContext context)
    : IProductRepository
{
    private readonly DbSet<Product> _dbSet = context.Set<Product>();

    public async Task<Product?> GetByIdAsync(object id, CancellationToken ct)
        => await _dbSet.FindAsync([id], ct);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct)
        => await _dbSet.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> ListAsync(
        ISpecification<Product> spec,
        CancellationToken ct)
        => await ApplySpec(spec).ToListAsync(ct);

    public async Task<int> CountAsync(
        ISpecification<Product> spec,
        CancellationToken ct)
        => await ApplySpec(spec, includePaging: false).CountAsync(ct);

    public async Task AddAsync(Product entity, CancellationToken ct)
        => await _dbSet.AddAsync(entity, ct);

    public Task AddRangeAsync(IEnumerable<Product> entities, CancellationToken ct)
        => _dbSet.AddRangeAsync(entities, ct);

    public Task UpdateAsync(Product entity, CancellationToken ct)
    {
        _dbSet.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Product entity, CancellationToken ct)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    private IQueryable<Product> ApplySpec(
        ISpecification<Product> spec, bool includePaging = true)
    {
        var query = spec.AsNoTracking ? _dbSet.AsNoTracking() : _dbSet;

        if (spec.Filter is not null)
            query = query.Where(spec.Filter);

        foreach (var include in spec.Includes)
            query = query.Include(include);

        if (spec.OrderBy is not null)
            query = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDesc is not null)
            query = query.OrderByDescending(spec.OrderByDesc);

        if (includePaging)
        {
            if (spec.Skip is { } skip)
                query = query.Skip(skip);
            if (spec.Take is { } take)
                query = query.Take(take);
        }

        return query;
    }
}
