---
sidebar_position: 5
---

# modulus generate-crud

Generates complete CRUD operations for an entity.

## Usage

```bash
modulus generate-crud <Entity> --module <Module> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--module` | Target module name (required) |
| `--dry-run` | Preview without writing files |

## What It Generates

### Entity

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = default!;
    public decimal Price { get; private set; }

    private Product() { }

    public Product(string name, decimal price)
    {
        Name = name;
        Price = price;
    }
}
```

### Repository

```csharp
public interface IProductRepository : IRepository<Product> { }

public class ProductRepository : EfRepository<Product>, IProductRepository
{
    public ProductRepository(CatalogDbContext context) : base(context) { }
}
```

### DTOs

```csharp
public sealed record ProductDto(Guid Id, string Name, decimal Price);
public sealed record CreateProductRequest(string Name, decimal Price);
public sealed record UpdateProductRequest(string Name, decimal Price);
```

### Commands & Handlers

```csharp
public sealed record CreateProduct(string Name, decimal Price) : ICommand<ProductDto>;
public sealed record UpdateProduct(Guid Id, string Name, decimal Price) : ICommand<ProductDto>;
public sealed record DeleteProduct(Guid Id) : ICommand;
```

### Queries & Handlers

```csharp
public sealed record GetAllProducts : IQuery<List<ProductDto>>;
public sealed record GetProductById(Guid Id) : IQuery<ProductDto>;
```

### Integration Event

```csharp
public sealed record ProductCreatedEvent : IIntegrationEvent
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = default!;
    public decimal Price { get; init; }
}
```

### API Endpoint

```csharp
[ApiController]
[Route("api/products")]
public sealed class ProductController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetAll()
        => await _mediator.QueryAsync(new GetAllProducts());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id)
        => await _mediator.QueryAsync(new GetProductById(id));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request)
        => await _mediator.SendAsync(new CreateProduct(request.Name, request.Price));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, UpdateProductRequest request)
        => await _mediator.SendAsync(new UpdateProduct(id, request.Name, request.Price));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.SendAsync(new DeleteProduct(id));
        return NoContent();
    }
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
