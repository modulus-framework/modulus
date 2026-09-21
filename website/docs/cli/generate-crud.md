---
sidebar_position: 5
---

# modulus generate-crud

Generates complete CRUD operations for an entity.

## Usage

```bash
modulus generate-crud <Entity> [-m|--module <Module>] [options]
```

## Options

| Option | Description |
|--------|-------------|
| `-m, --module` | Target module name. Auto-detected when the app has a single module |
| `--dry-run` | Preview without writing files |

## What It Generates

Files are distributed across the module's layer projects (existing files are
reported as skipped, never overwritten):

### Entity (`Domain`)

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
}
```

### Repository (`Domain` interface + `Infrastructure` implementation)

```csharp
// Domain: IProductRepository.cs
public interface IProductRepository : IRepository<Product> { }

// Infrastructure: ProductRepository.cs — standalone spec-based repo
// (DbSet + ISpecification methods), auto-wired in the module composition root
public sealed class ProductRepository(CatalogDbContext context) : IProductRepository { ... }
```

### DTO (`Application/Dtos`)

```csharp
public sealed class ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
```

### Commands & Queries (`Application`, flat — no `Commands/`/`Queries/` subfolders)

```csharp
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

public sealed class CreateProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        var entity = new Product { Name = command.Name };
        await repo.AddAsync(entity, ct);
        await unitOfWork.CommitAsync(ct);
        return entity.Id;
    }
}
```

Plus `Update`/`Delete` commands and `Get{Entities}`/`Get{Entity}ById` queries with handlers.

### Integration Event (`Application/IntegrationEvents`)

```csharp
public sealed record ProductCreatedIntegrationEvent(Guid Id)
    : IntegrationEventBase("catalog.product-created.v1");
```

### API Endpoints (`Presentation/{Entities}Endpoint.cs`)

REPR-style endpoints (no MVC controllers), all with `RequireAuthorization()`:

```csharp
public sealed class GetProductsEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<ProductDto>>
{
    public override void Configure()
    {
        Get("/api/catalog/products");
        RequireAuthorization();
    }
    // ... GetProductById / Create / Update / Delete endpoints included
}
```

### DbContext Update

```csharp
// Automatically adds DbSet<Product> to the module's DbContext
public DbSet<Product> Products => Set<Product>();
```

## Example

```bash
modulus generate-crud Product --module Catalog
modulus generate-crud Order --module Orders
modulus generate-crud Customer --module Customers
```

## See Also

- [`generate-command`](generate-command) — Generate single command
- [`generate-query`](generate-query) — Generate single query
