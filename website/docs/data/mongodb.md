---
sidebar_position: 3
---

# MongoDB

Modulus supports MongoDB as an alternative to relational databases.

## Setup

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddModuleDatabase<CatalogMongoContext>(config);
}
```

## ModuleMongoContext

```csharp
public sealed class CatalogMongoContext : ModuleMongoContext
{
    public IMongoCollection<Product> Products =>
        GetCollection<Product>("products");

    public CatalogMongoContext(IOptions<MongoDbOptions> options)
        : base(options) { }
}
```

## MongoRepository

```csharp
public sealed class ProductRepository : MongoRepository<Product>, IProductRepository
{
    public ProductRepository(CatalogMongoContext context) : base(context) { }

    public async Task<Product?> GetByNameAsync(string name, CancellationToken ct)
    {
        return await Collection
            .Find(p => p.Name == name)
            .FirstOrDefaultAsync(ct);
    }
}
```

## Tenant Filtering

MongoDB repositories automatically apply tenant filtering:

```csharp
// The MongoTenantFilter intercepts queries and adds tenantId filter
// when ICurrentTenant is available
public class MongoTenantFilter<TDocument> : IClientSessionHandle
{
    // Automatically filters by tenantId
}
```

## Health Checks

MongoDB includes built-in health checks:

```csharp
// Registered automatically with AddModuleDatabase<MongoContext>
services.AddHealthChecks()
    .AddMongoDb(connectionString: "mongodb://localhost:27017");
```

## Considerations

| Aspect | Recommendation |
|--------|----------------|
| **Schema** | Use DTOs/projections rather than entity inheritance |
| **Transactions** | Use MongoDB transactions for multi-document atomicity |
| **Indexing** | Define indexes in `OnModelCreating` or via driver |
| **Migrations** | Manual schema evolution (no migration framework) |
