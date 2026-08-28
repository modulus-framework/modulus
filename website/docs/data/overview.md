---
sidebar_position: 1
---

# Data Layer Overview

Modulus provides a flexible data access layer supporting relational databases (via EF Core) and MongoDB.

## Supported Providers

| Provider | Package | Use Case |
|----------|---------|----------|
| **SQLite** | `Modulus.Data.SQLite` | Development, testing, desktop apps |
| **SQL Server** | `Modulus.Data.SqlServer` | Enterprise, Windows environments |
| **PostgreSQL** | `Modulus.Data.PostgreSQL` | Open-source, cloud-native |
| **MySQL** | `Modulus.Data.MySQL` | Web applications, cloud |
| **MongoDB** | `Modulus.Data.MongoDB` | Document storage, high throughput |

## Per-Module Databases

Each module owns its own `DbContext` and database:

```
Module A → CatalogDb (PostgreSQL)
Module B → OrdersDb (SQL Server)
Module C → InventoryDb (SQLite)
```

This ensures:

- **Independent schema evolution** per module
- **No cross-module joins** (forces loose coupling)
- **Independent scaling** when extracting to microservices
- **Module-level transactions** (no distributed transactions needed)

## Configuration

Connection strings are configured per module in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Catalog": "Data Source=catalog.db",
    "Orders": "Server=localhost;Database=Orders;Trusted_Connection=true;",
    "Inventory": "Host=localhost;Database=Inventory"
  }
}
```

## Registration

Modules register their database in their composition root:

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddModuleDatabase<CatalogDbContext>(config);
}
```

The `AddModuleDatabase<TContext>` method:

1. Registers the DbContext with provider-specific configuration
2. Registers generic `IRepository<T>` implementations
3. Exposes the context as `DbContext` for transaction behaviors

## See Also

- [Entity Framework](entity-framework) — EF Core integration details
- [MongoDB](mongodb) — MongoDB document storage
- [Repositories](repositories) — Repository pattern
- [Migrations](migrations) — Database schema management
