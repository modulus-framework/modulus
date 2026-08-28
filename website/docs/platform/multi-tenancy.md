---
sidebar_position: 2
---

# Multi-Tenancy

Modulus provides built-in multi-tenant data isolation.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Request Pipeline                          │
│                                                              │
│  1. TenantMiddleware resolves tenant                         │
│     ├── Header: X-Tenant-Id                                 │
│     ├── Claim: tenant_id                                    │
│     ├── Subdomain: {tenant}.app.com                         │
│     └── Route: /api/{tenant}/...                            │
│                                                              │
│  2. ICurrentTenant populated (AsyncLocal)                    │
│                                                              │
│  3. EF Core query filter: WHERE TenantId = @tenantId        │
│                                                              │
│  4. TenantId stamped on new entities                         │
└─────────────────────────────────────────────────────────────┘
```

## Setup

```csharp
services.AddModulusMultiTenancy(config);
```

## Resolution

Tenants are resolved from (in order):

| Source | Config Key | Example |
|--------|------------|---------|
| **Header** | `X-Tenant-Id` | `X-Tenant-Id: 550e8400-e29b-41d4-a716-446655440000` |
| **Claim** | `tenant_id` | JWT claim |
| **Subdomain** | `{tenant}.app.com` | URL subdomain |
| **Route** | `/api/{tenant}/...` | URL segment |

## ICurrentTenant

```csharp
public sealed class GetProductsHandler(ICurrentTenant tenant)
    : IQueryHandler<GetProducts, List<ProductDto>>
{
    public async Task<List<ProductDto>> HandleAsync(
        GetProducts query, CancellationToken ct)
    {
        var tenantId = tenant.Id; // null when in host context
        // Query automatically filtered by EF Core
    }
}
```

## Changing Tenant

```csharp
using (tenant.Change(tenantId))
{
    // All queries within this scope are filtered by tenantId
    var products = await repository.ListAsync(ct);
}
```

## Data Isolation

EF Core query filters automatically apply:

```csharp
// In ModuleDbContext
modelBuilder.Entity<Product>().HasQueryFilter(
    p => !p.IsDeleted && p.TenantId == _currentTenantId);
```

## NoSQL Support

MongoDB repositories apply tenant filtering:

```csharp
// MongoTenantFilter adds { tenantId: X } to all queries
public class MongoTenantFilter<TDocument> : IClientSessionHandle
{
    // Automatically applied by MongoRepository
}
```

## Host Context

When no tenant is in scope (host-level operations):

```csharp
var tenantId = tenant.Id; // null
// EF Core matches all tenants (no filter applied)
```

## See Also

- [Authorization](authorization) — Per-tenant permissions
- [Entity Framework](../data/entity-framework) — Query filters
