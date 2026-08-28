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

// Register in module
services.AddPermissions(builder =>
{
    builder.Register(CatalogPermissions.ProductsView, "View products");
    builder.Register(CatalogPermissions.ProductsCreate, "Create products");
});
```

## Using Permissions

### In Controllers

```csharp
[ApiController]
[Route("api/products")]
[Authorize(Policy = "catalog.products.create")]
public sealed class ProductController : ControllerBase
{
    // Only users with catalog.products.create permission can access
}
```

### In Handlers

```csharp
public sealed class CreateProductHandler(IAuthorizationService auth)
    : ICommandHandler<CreateProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
    {
        await auth.AuthorizeAsync(CatalogPermissions.ProductsCreate);

        // Proceed if authorized
        var product = new Product(command.Name, command.Price);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

## Permission Registry

```csharp
public sealed class CatalogModule : ModulusModule
{
    public override void PreConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddPermissions(builder =>
        {
            builder.Register("catalog.products.view", "View products");
            builder.Register("catalog.products.create", "Create products");
        });
    }
}
```

## Organization Scoping

Permissions can be scoped to organizational units:

```csharp
public sealed class OrgScopedPermission : IOrgUnitScoped
{
    public Guid OrgUnitId { get; set; }
    public string Permission { get; set; } = default!;
}
```

## Field-Level Security

```csharp
[FieldAuthorization("salary")]
public decimal Salary { get; set; }
```

## See Also

- [Multi-Tenancy](multi-tenancy) — Per-tenant isolation
- [External Providers](../identity/external-providers) — IdP integration
