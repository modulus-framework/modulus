---
sidebar_position: 3
---

# MongoDB

`Modulus.Data.MongoDB` is an alternative to a relational context for a module. It provides a context base class, a repository base class, tenant-scoped collections and the outbox and inbox stores for MongoDB.

## Setup

```csharp
services.AddMongoDatabase(o =>
{
    o.ConnectionString = config.GetConnectionString("Catalog")!;
    o.DatabaseName = "catalog";
    o.CollectionPrefix = "catalog_";
});
```

`AddMongoDatabase` takes a configure callback. Read the connection string from configuration in the callback. For one database per tenant, use `AddMongoDatabasePerTenant(o => ..., id => $"shop_{id:N}")`.

## Module context

A module context derives from `ModuleMongoContext`. It exposes its collections and gets the tenant from `ICurrentTenant`.

```csharp
public sealed class CatalogMongoContext : ModuleMongoContext
{
    public CatalogMongoContext(
        IMongoDatabase database,
        IOptions<MongoOptions> options,
        ICurrentTenant tenant)
        : base(database, options, tenant) { }

    public IMongoCollection<ProductDocument> Products => GetCollection<ProductDocument>("products");
}
```

Collections are `IMongoCollection<T>` from the MongoDB .NET driver. `GetTenantCollection<T>` returns a `TenantScopedCollection<T>` instead, which applies the tenant filter to every query and stamps the tenant on every insert.

## Repository

`MongoRepository<T, TDoc>` implements `IRepository<T>` for an aggregate `T`. The document type `TDoc` implements `IHasTenantId`, so the tenant can be enforced in the query.

```csharp
public sealed class ProductRepository : MongoRepository<Product, ProductDocument>
{
    public ProductRepository(IMongoCollection<ProductDocument> collection, ICurrentTenant tenant)
        : base(collection, tenant) { }
}
```

## Tenant filtering

Filtering is fail-closed, the same as the EF Core path:

- In the host context (multi-tenancy off, or `ICurrentTenant.Change(null)`), queries see all documents.
- With no resolved tenant, queries match nothing.
- Inside a tenant, `MongoTenantFilter.For<T>(tenant)` adds the tenant condition to the query.

Use `TenantScopedCollection<T>.Unscoped(reason)` only when a query really needs every tenant. The call is recorded in the security audit.

## Outbox and inbox

The outbox and inbox have MongoDB stores:

```csharp
services.AddMongoOutbox();    // collection "outbox"
services.AddMongoInbox();     // collection "inbox"
```

The outbox joins the caller's Mongo session, when the unit of work supplies one, so an event is written in the same transaction as the change on a replica set.

## Health

`MongoHealthCheck` implements `IModuleHealthCheck`, so registering it adds the database to the module health endpoints.

```csharp
services.AddSingleton<IModuleHealthCheck, MongoHealthCheck>();
```

## Considerations

| Aspect | Recommendation |
|--------|----------------|
| **Schema** | Use separate document types, not the aggregate type, so the persisted shape can change independently |
| **Transactions** | Use MongoDB transactions for multi-document atomicity (replica set required) |
| **Indexing** | Create indexes in the module's startup, through the driver |
| **Migrations** | Manual schema evolution; no migration framework |
