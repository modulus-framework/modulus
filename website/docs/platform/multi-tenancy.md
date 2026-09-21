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
│  1. TenantMiddleware tries resolvers in registration order   │
│     ├── JWT claim: tid (recommended first — see below)      │
│     ├── Header: X-Tenant-Id                                 │
│     └── Subdomain: {tenant}.{baseDomain}                    │
│     First non-null result wins                               │
│                                                              │
│  2. If the caller is authenticated and a JWT-claim resolver  │
│     is configured, its claim-derived tenant is cross-checked │
│     against whichever tenant resolved in step 1 — a mismatch │
│     is rejected (403), not silently trusted (see below)      │
│                                                              │
│  3. ICurrentTenant populated (static AsyncLocal — flows     │
│     into background jobs and message consumers too)          │
│                                                              │
│  4. EF Core query filter: tenant rows only (fail-closed)    │
│                                                              │
│  5. TenantId stamped on new entities                         │
└─────────────────────────────────────────────────────────────┘
```

## Setup

```csharp
services.AddMultiTenancy(builder => builder
    .UseJwtClaimResolver()            // JWT "tid" claim — prefer this first
    .UseHeaderResolver()              // X-Tenant-Id
    .UseSubdomainResolver("app.com")  // {tenant}.app.com
);
```

Resolvers are tried in registration order; the first non-null result wins.
There is no route-segment resolver. For an EF-backed tenant store:

```csharp
services.AddEfCoreTenantStore(options => options.UseSqlite(connection));
```

## Resolver Security

`HeaderTenantResolver` trusts the `X-Tenant-Id` header **unconditionally** —
it has no way to know whether the value came from a trusted edge component
(an API gateway, Azure Front Door, Cloudflare Access) that authenticated the
caller and mapped them to a tenant, or from the caller itself. Never expose it
to a client you don't fully trust to set that header honestly.

When `UseJwtClaimResolver()` is also configured, `TenantMiddleware`
cross-checks an authenticated caller's claim-derived tenant against whichever
tenant actually resolved (from any resolver) and **rejects the request with
403** on a mismatch — closing the gap where a caller authenticated to tenant A
sends `X-Tenant-Id: <B>` and would otherwise silently run as tenant B. This
check only fires when both a `JwtClaimTenantResolver` is registered *and* the
caller is authenticated with that claim present; a purely header-driven,
trusted-edge deployment with no JWT resolver configured is unaffected.

## ICurrentTenant

```csharp
public interface ICurrentTenant
{
    Guid? TenantId { get; }
    string? TenantSlug { get; }
    bool IsAvailable { get; }
    bool IsHost { get; }
    IDisposable Change(TenantInfo? tenant);
}
```

`IsHost` is true only when multi-tenancy is off or code explicitly entered
the host scope — it is the seam that makes filtering **fail-closed** (see below).

```csharp
public sealed class GetProductsHandler(ICurrentTenant tenant, IProductRepository repo)
    : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> HandleAsync(
        GetProductsQuery query, CancellationToken ct)
    {
        // Queries are automatically filtered by EF Core — no manual check needed.
        return await repo.ListAsync(new AllProductsSpec(), ct);
    }
}
```

## Changing Tenant

Background jobs, message consumers, and hosted services run outside HTTP and
must establish a tenant explicitly:

```csharp
using (tenant.Change(new TenantInfo(tenantId, "acme")))
{
    // All queries within this scope see this tenant
    var products = await repository.ListAsync(spec, ct);
}

using (tenant.Change(null))
{
    // Explicit, privileged host scope — sees ALL tenants
}
```

## Data Isolation

The query filter captures the `ICurrentTenant` **service** (re-evaluated per
query) and combines soft-delete + tenant predicates — never capture a tenant
*value* into the model (it would freeze the first request's tenant into the
cached model and leak across tenants):

```csharp
// In ModuleDbContext (simplified)
modelBuilder.Entity<Product>().HasQueryFilter(p =>
    !p.IsDeleted && (currentTenant.IsHost || p.TenantId == currentTenant.TenantId));
```

Fail-closed rule: multi-tenancy on but **no tenant resolved** → filters match
**nothing** (never all rows). Seeing all tenants requires the deliberate
`Change(null)` host scope.

## NoSQL Support

MongoDB repositories apply tenant filtering via the static helper:

```csharp
// MongoTenantFilter.For<T>(tenant) adds { tenantId: X } to queries.
// Host scope sees all; unresolved tenant matches nothing.
var filter = MongoTenantFilter.For<Product>(currentTenant);
```

## Host Context

```csharp
tenant.IsHost;     // true only when multi-tenancy is off or inside Change(null)
tenant.TenantId;   // null in host context
```

An unresolved tenant is **not** the host context: unresolved sees nothing,
host sees everything.

## Tenant Store

`ITenantStore` looks tenants up by id/slug and enumerates them for fan-out:

```csharp
public interface ITenantStore
{
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct); // default: empty
}
```

`EfTenantStore` returns active tenants in slug order. Deactivated tenants
resolve as `null` (fail-closed).

## Per-Tenant Databases

Resolve the connection string per scope and migrate per tenant:

```csharp
// Per-tenant connection resolver (scoped options)
services.AddModuleDatabase<CatalogDbContext>(
    sp => sp.GetRequiredService<ICurrentTenant>().ConnectionString ?? hostConnection,
    options => options.UseNpgsql(...));

// Migrator job / init container (not every replica)
await services.MigrateModulusDatabasesForTenantsAsync(DatabaseInitializationMode.Migrate);
```

## See Also

- [Authorization](authorization) — Per-tenant permissions
- [Entity Framework](../data/entity-framework) — Query filters
- [Migrations](../data/migrations) — Per-tenant migration fan-out
