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

Use specifications for complex queries:

```csharp
public sealed class ProductsByCategorySpec : ISpecification<Product>
{
    public Expression<Func<Product, bool>> Criteria { get; }

    public ProductsByCategorySpec(string category)
    {
        Criteria = p => p.Category == category;
    }
}

// Usage
var products = await repository.ListAsync(new ProductsByCategorySpec("Electronics"));
```
