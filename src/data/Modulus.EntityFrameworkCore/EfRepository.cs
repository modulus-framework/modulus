namespace Modulus.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions.Common;
using Modulus.Data.Abstractions;

public class EfRepository<T>(DbContext db)
    : IRepository<T> where T : class
{
    protected readonly DbSet<T> Set = db.Set<T>();

    public Task<T?> GetByIdAsync(object id, CancellationToken ct)
        => Set.FindAsync([id], ct).AsTask();

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).ToListAsync(ct);

    public Task<int> CountAsync(
        ISpecification<T> spec, CancellationToken ct)
        => ApplySpec(Set.AsQueryable(), spec).CountAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct)
        => await Set.AddAsync(entity, ct);

    public async Task AddRangeAsync(
        IEnumerable<T> entities, CancellationToken ct)
        => await Set.AddRangeAsync(entities, ct);

    public Task UpdateAsync(T entity, CancellationToken ct)
    {
        db.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct)
    {
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    protected static IQueryable<T> ApplySpec(
        IQueryable<T> query, ISpecification<T> spec)
    {
        if (spec.Filter     != null) query = query.Where(spec.Filter);
        if (spec.OrderBy    != null) query = query.OrderBy(spec.OrderBy);
        if (spec.OrderByDesc!= null) query = query.OrderByDescending(spec.OrderByDesc);
        foreach (var inc in spec.Includes) query = query.Include(inc);
        if (spec.Skip       != null) query = query.Skip(spec.Skip.Value);
        if (spec.Take       != null) query = query.Take(spec.Take.Value);
        if (spec.AsNoTracking)       query = query.AsNoTracking();
        return query;
    }
}

public class EfReadRepository<T>(DbContext db)
    : EfRepository<T>(db), IReadRepository<T>
    where T : class
{
    public async Task<PagedList<TResult>> ListPagedAsync<TResult>(
        ISpecification<T> spec,
        Func<T, TResult> selector,
        int page, int size,
        CancellationToken ct)
    {
        var baseQuery = ApplySpec(Set.AsQueryable(), spec);
        var total     = await baseQuery.CountAsync(ct);
        var items     = await baseQuery
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return new PagedList<TResult>
        {
            Items      = items.Select(selector).ToList().AsReadOnly(),
            TotalCount = total,
            Page       = page,
            PageSize   = size,
        };
    }

    public async Task<bool> AnyAsync(
        ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).AnyAsync(ct);
}