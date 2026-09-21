---
sidebar_position: 3
---

# Authorization

Modulus provides a permission-based authorization system.

## Concepts

| Concept | Description |
|---------|-------------|
| **Permission** | A named access right (e.g., `catalog.products.create`) |
| **Permission Registry** | Central registry of all permissions |
| **Policy** | Named set of requirements |
| **Requirement** | A condition that must be satisfied |

## Setup

```csharp
services.AddModulusAuthorization(config);
```

## Defining Permissions

```csharp
public static class CatalogPermissions
{
    public const string Products = "catalog.products";
    public const string ProductsView = "catalog.products.view";
    public const string ProductsCreate = "catalog.products.create";
    public const string ProductsEdit = "catalog.products.edit";
    public const string ProductsDelete = "catalog.products.delete";
}

// Register in module (declarations replay against the registry at startup)
services.AddPermissions("Catalog", registry =>
{
    registry.Add(CatalogPermissions.ProductsView, "View products");
    registry.Add(CatalogPermissions.ProductsCreate, "Create products");
});
```

## Using Permissions

### In Endpoints

REPR endpoints declare requirements via `RequireAuthorization`, MVC actions
via `[Authorize(Policy = "...")]`:

```csharp
// REPR
Get("/api/catalog/products");
RequireAuthorization("catalog.products.create");

// MVC
[Authorize(Policy = "catalog.products.create")]
public sealed class CreateProductEndpoint : ControllerBase { }
```

Only users holding the permission can access the endpoint.

### In Handlers

```csharp
public sealed class CreateProductHandler(IAuthorizationService auth, ...)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        await auth.AuthorizeAsync(CatalogPermissions.ProductsCreate);

        // Proceed if authorized
        var product = new Product { Name = command.Name };
        ...
    }
}
```

## Permission Registry

```csharp
public sealed class CatalogModule : ModulusModule
{
    public override void PreConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddPermissions("Catalog", registry =>
        {
            registry.Add("catalog.products.view", "View products");
            registry.Add("catalog.products.create", "Create products");
        });
    }
}
```

The registry (`Add`/`GetAll`/`GetByModule`/`Exists`) freezes after startup —
declare everything in `ConfigureServices`. Grants are fetched once per request
and passed to the resolver (single DB read).

## Organization Scoping

Org-unit reads go through `IHasOrgUnit` entities + `ResourceAttributes`
(evaluated by `ResourceAuthorizer`); delegations, SoD policies, and access
recertification live in the Governance model (`IDelegationStore`,
`EffectiveAccessService`).

## Field-Level Security

Field classification + masking profiles (`FieldSecurityProfile`,
`FieldAuthorizer`) control per-field read/write/mask decisions — see the
`Modulus.Platform.Authorization.Fields` namespace.

## See Also

- [Multi-Tenancy](multi-tenancy) — Per-tenant isolation
- [External Providers](../identity/external-providers) — IdP integration
