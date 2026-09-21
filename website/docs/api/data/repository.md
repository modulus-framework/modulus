---
sidebar_position: 5
---

# Repository API

See [Repositories](../../data/repositories) for the full guide. Reference:

## IRepository\<T\>

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct); // composite-PK aware
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct);
    Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct);
    Task<T> SingleAsync(ISpecification<T> spec, CancellationToken ct);
    Task<T?> SingleOrDefaultAsync(ISpecification<T> spec, CancellationToken ct);
    IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> spec);
    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task AddAsync(T entity, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct);
    Task UpdateAsync(T entity, CancellationToken ct);
    Task DeleteAsync(T entity, CancellationToken ct);
    Task DeleteRangeAsync(ISpecification<T> spec, CancellationToken ct);
}
```

## IReadRepository\<T\>

```csharp
public interface IReadRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct);
    Task<PagedList<TResult>> ListPagedAsync<TResult>(
        ISpecification<T> spec,
        Expression<Func<T, TResult>> selector,
        int page, int size,
        CancellationToken ct);
    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task<bool> AnyAsync(ISpecification<T> spec, CancellationToken ct);
}
```

## ISpecification\<T\>

```csharp
public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Filter { get; }
    List<IncludeChain<T>>? IncludeChains { get; }
    List<OrderByClause<T>>? OrderByClauses { get; }
    int? Skip { get; }
    int? Take { get; }
    bool AsSplitQuery { get; }
    bool IgnoreQueryFilters { get; }
    string? Tag { get; }
    bool AsNoTracking { get; }
    ISpecification<T> And(Expression<Func<T, bool>> other);
    ISpecification<T> Or(Expression<Func<T, bool>> other);
    ISpecification<T> Not();
}
```

## EfRepository\<T\>

Constructed with `IServiceProvider`, not a `DbContext` — it resolves the
owning module context at runtime via `IEntityContextMap`:

```csharp
public class EfRepository<T>(IServiceProvider services) : IRepository<T> where T : class
{
    // GetByIdAsync uses filter-honoring EF.Property LINQ
    // (composite-PK aware) — explicitly not FindAsync
}
```
