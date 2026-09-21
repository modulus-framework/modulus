---
sidebar_position: 3
---

# First Module

This guide walks you through creating a business module with CRUD operations.

## 1. Add a Module

```bash
modulus add-module Products
```

This creates four projects under `src/Modules/MyApp.Modules.Products/`:

| Layer | Project | Contains |
|-------|---------|----------|
| **Domain** | `.Domain` | Entity, `IRepository` |
| **Application** | `.Application` | `IUnitOfWork`, commands/queries, DTOs |
| **Infrastructure** | `.Infrastructure` | `ProductsDbContext`, repository impl, module composition |
| **Presentation** | `.Presentation` | API endpoints |

## 2. Generate CRUD

```bash
modulus generate-crud Product --module Products
```

This generates:

- **Entity** (`Product`) — `AggregateRoot<Guid>` with a `Name`
- **Repository** interface (`IProductRepository`) and spec-based implementation
- **DTO** (`ProductDto` with `Id` + `Name`) under `Application/Dtos/`
- **Commands** (`CreateProductCommand`, …) + handlers using `IUnitOfWork.CommitAsync`
- **Queries** (`GetProductsQuery`, `GetProductByIdQuery`) + handlers
- **Integration Event** (`ProductCreatedIntegrationEvent`) under `Application/IntegrationEvents/`
- **API Endpoints** (REPR `Endpoint<>` classes with `RequireAuthorization()`)
- **DbContext mapping** (auto-wired `DbSet<Product>`)

## 3. Implement Business Logic

Edit the command handlers in `Application/`:

```csharp
public sealed class CreateProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateProductCommand command,
        CancellationToken ct)
    {
        var product = new Product { Name = command.Name };
        await repo.AddAsync(product, ct);
        await unitOfWork.CommitAsync(ct);
        return product.Id;
    }
}
```

## 4. Run Migrations

```bash
modulus migrate add InitialCreate
modulus migrate update
```

## 5. Test the API

```bash
# Create a product
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Widget", "price": 9.99}'

# List products
curl http://localhost:5000/api/products
```

## Module Lifecycle

Modules can hook into the application lifecycle:

```csharp
public sealed class ProductsModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddModuleDatabase<ProductsDbContext>(options =>
            options.UseSqlite(config.GetConnectionString("Products")
                ?? "Data Source=products.db"));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProductsDbContext>());
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }

    public override async Task InitializeAsync(ModuleContext context)
    {
        // Run after host builds (seeding, migrations, etc.)
        var db = context.ServiceProvider.GetRequiredService<ProductsDbContext>();
        await db.Database.MigrateAsync();
    }

    public override async Task ShutdownAsync()
    {
        // Cleanup on graceful shutdown
    }
}
```

## Next Steps

- [Module System](../architecture/module-system) — Deep dive into module dependencies
- [Data Layer](../data/overview) — Database configuration and repositories
- [Messaging](../messaging/overview) — Event-driven communication
