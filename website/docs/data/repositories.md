---
sidebar_position: 4
---

# Repositories

Modulus uses the Repository pattern to abstract data access.

## Interfaces

### IRepository\<T\> (Write Side)

```csharp
public interface IRepository<T> where T : class
{
    // object id: composite PKs supported; filter-honoring (no FindAsync)
    Task<T?> GetByIdAsync(object id, CancellationToken ct);
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

### IReadRepository\<T\> (Read Side)

```csharp
public interface IReadRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct);
    // Server-side projection: only projected columns are materialized
    Task<PagedList<TResult>> ListPagedAsync<TResult>(
        ISpecification<T> spec,
        Expression<Func<T, TResult>> selector,
        int page, int size,
        CancellationToken ct);
    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task<bool> AnyAsync(ISpecification<T> spec, CancellationToken ct);
}
```

## Usage

### Define a Repository Interface

```csharp
public interface IProductRepository : IRepository<Product>
{
    Task<Product?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetByCategoryAsync(string category, CancellationToken ct = default);
}
```

### Implement with EF Core

Generated modules ship a standalone spec-based repository (not an
`EfRepository` subclass). The framework's generic `EfRepository<T>`
resolves the owning module context at runtime via `IEntityContextMap`:

```csharp
public sealed class ProductRepository(CatalogDbContext context) : IProductRepository
{
    private readonly DbSet<Product> _dbSet = context.Set<Product>();

    public async Task<Product?> GetByNameAsync(string name, CancellationToken ct)
        => await _dbSet.FirstOrDefaultAsync(p => p.Name == name, ct);
}
```

### Register in Module

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddModuleDatabase<CatalogDbContext>(options =>
        options.UseSqlite(config.GetConnectionString("Catalog")));
    services.AddScoped<IProductRepository, ProductRepository>();
}
```

### Use in Handlers

```csharp
public sealed class GetProductByIdHandler(IProductRepository repository)
    : IQueryHandler<GetProductById, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductById query, CancellationToken ct)
    {
        var product = await repository.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(Product), query.Id);

        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

## EfRepository\<T\>

The framework provides a generic `EfRepository<T>` that:

1. Routes to the correct `DbContext` via `IEntityContextMap`
2. Provides basic CRUD operations
3. Supports `IQueryable` access for custom queries

```csharp
// EfRepository is automatically registered for each entity
// when you call AddModuleDatabase<TContext>()
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Catalog")));
// Registers IRepository<Product>, IRepository<Order>, etc.
```

## Specification Pattern

Use specifications for complex queries with composable operators.
Set `Filter` in the constructor; add ordering/includes with the protected
helpers:

```csharp
public sealed class ProductsByCategorySpec : Specification<Product>
{
    public ProductsByCategorySpec(string category)
    {
        Filter = p => p.Category == category;
        AddOrderBy(p => p.CreatedAt);
        AddInclude(p => p.Category);
        AsSplitQuery = true; // Prevent cartesian explosion
    }
}

// Usage
var spec = new ProductsByCategorySpec("Electronics");
var products = await repository.ListAsync(spec, ct);

// Composable combinators (mutate and return the same spec)
var spec2 = baseSpec
    .And(p => p.Active)
    .Or(p => p.Featured)
    .Not();
```

Available surface: `Filter`, `IncludeChains` (with ThenInclude),
`OrderByClauses` (multiple, ThenBy), `Skip`/`Take`, `AsSplitQuery`,
`IgnoreQueryFilters`, `Tag`, `AsNoTracking`.

## Server-Side Projection

Project directly in the query without materializing full entities:

```csharp
// Specification with projection
public sealed class ProductListDtoSpec : Specification<Product, ProductListDto>
{
    public ProductListDtoSpec()
    {
        ProjectionExpression = p => new ProductListDto
        {
            Id = p.Id,
            Name = p.Name
        };
        AddOrderBy(p => p.Id);
    }
}
```

Or project ad-hoc with `ListPagedAsync` (note argument order —
spec first, then selector):

```csharp
// Returns DTOs, not full Product entities
var dtos = await repository.ListPagedAsync(
    spec,
    (Product p) => new ProductListDto
    {
        Id = p.Id,
        Name = p.Name
    },
    page: 1, size: 10, ct);
```

## New Repository Methods

**Single Row Operations:**
```csharp
// Single row with default
var product = await repository.FirstOrDefaultAsync(spec);

// Single row (throws if 0 or 2+)
var product = await repository.SingleAsync(spec);

// Single row or null
var product = await repository.SingleOrDefaultAsync(spec);
```

**Streaming & Bulk Operations:**
```csharp
// Stream large result sets
await foreach (var product in repository.AsAsyncEnumerable(spec))
{
    // Process one at a time
}

// Bulk delete matching a spec (tenant/soft-delete filters respected)
await repository.DeleteRangeAsync(spec);
```

## Specification Validation

Paging requires ordering to be deterministic (`Skip`/`Take` without an
`OrderBy` clause throws `InvalidOperationException`):

```csharp
public sealed class PagedSpec : Specification<Product>
{
    public PagedSpec()
    {
        AddOrderBy(p => p.Id);
        Skip = 10;
        Take = 20;
    }
}
// ✓ Valid: OrderBy is set
```
