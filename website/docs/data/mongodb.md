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
// MongoTenantFilter.For<T>(tenant) adds { tenantId: X } to all queries.
// Fail-closed like EF Core: host scope (multi-tenancy off or explicit
// Change(null)) sees all; an unresolved tenant matches nothing.
var filter = MongoTenantFilter.For<Product>(currentTenant);
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
