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
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}
```

### IReadRepository\<T\> (Read Side)

```csharp
public interface IReadRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
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

```csharp
public sealed class ProductRepository : EfRepository<Product>, IProductRepository
{
    public ProductRepository(CatalogDbContext context) : base(context) { }

    public async Task<Product?> GetByNameAsync(string name, CancellationToken ct)
    {
        return await Context.Products
            .FirstOrDefaultAsync(p => p.Name == name, ct);
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(string category, CancellationToken ct)
    {
        return await Context.Products
            .Where(p => p.Category == category)
            .ToListAsync(ct);
    }
}
```

### Register in Module

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddModuleDatabase<CatalogDbContext>(config);
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
services.AddModuleDatabase<CatalogDbContext>(config);
// Registers IRepository<Product>, IRepository<Order>, etc.
```

## Specification Pattern

Use specifications for complex queries with composable operators:

```csharp
public sealed class ProductsByCategorySpec : Specification<Product>
{
    public ProductsByCategorySpec(string category)
    {
        AddCriteria(p => p.Category == category);
        AddOrderBy(p => p.CreatedAt);
        AddInclude(p => p.Category);
        AddInclude(p => p.Tags);
        AddAsSplitQuery(); // Prevent cartesian explosion
    }
}

// Usage
var spec = new ProductsByCategorySpec("Electronics");
var products = await repository.ListAsync(spec);

// Composable combinators
var spec2 = baseSpec
    .And(p => p.Active)
    .Or(p => p.Featured)
    .Not(p => p.Discontinued);
```

## Fluent Specification Builder

Build specifications inline with chainable methods:

```csharp
var spec = new Specification<Product>()
    .WithCriteria(p => p.Category == "Electronics")
    .WithOrderBy(p => p.Price)
    .WithThenBy(p => p.Name)
    .WithInclude(p => p.Category)
    .WithThenInclude((Product p) => p.Category.Parent)
    .WithAsSplitQuery()
    .WithSkip(10)
    .WithTake(20);

var products = await repository.ListAsync(spec);
```

## Server-Side Projection

Project directly in the query without materializing full entities:

```csharp
// Specification with projection
public sealed class ProductListDtoSpec : Specification<Product, ProductListDto>
{
    public ProductListDtoSpec()
    {
        WithOrderBy(p => p.CreatedAt);
        WithInclude(p => p.Category);
    }
}

// Returns DTOs, not full Product entities
var dtos = await repository.ListPagedAsync(
    (Product p) => new ProductListDto
    {
        Id = p.Id,
        Name = p.Name,
        Category = p.Category.Name, // Joined in SQL
        Price = p.Price
    },
    spec, page: 1, pageSize: 10);
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

// Bulk delete with filters respected
await repository.DeleteRangeAsync(productsToDelete);

// Bulk update with tenant/soft-delete filters
var updated = await repository.ExecuteUpdateAsync(spec, 
    p => p.Price, 100m);
```

## Specification Validation

Paging requires ordering to be deterministic:

```csharp
var spec = new Specification<Product>()
    .WithOrderBy(p => p.Id)
    .WithSkip(10)
    .WithTake(20);
// ✓ Valid: OrderBy is set

var badSpec = new Specification<Product>()
    .WithSkip(10)
    .WithTake(20);
// ✗ Throws: Skip/Take require OrderBy
```
