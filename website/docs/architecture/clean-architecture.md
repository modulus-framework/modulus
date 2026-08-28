---
sidebar_position: 4
---

# Clean Architecture

Each Modulus module follows a 4-layer Clean Architecture layout:

```
MyApp.Modules.Catalog/
├── .Domain/              # Domain layer
│   ├── Product.cs        # Entity
│   └── IProductRepository.cs
├── .Application/         # Application layer
│   ├── IUnitOfWork.cs
│   ├── Dtos/
│   ├── Commands/
│   ├── Queries/
│   └── IntegrationEvents/
├── .Infrastructure/      # Infrastructure layer
│   ├── CatalogDbContext.cs
│   ├── ProductRepository.cs
│   └── CatalogModule.cs  # Composition root
└── .Presentation/        # Presentation layer
    └── ProductController.cs
```

## Layer Responsibilities

### Domain Layer

The innermost layer with no dependencies on other layers:

| Contains | Examples |
|----------|----------|
| Entities | `Product`, `Order`, `Customer` |
| Value objects | `Money`, `Address`, `Quantity` |
| Repository interfaces | `IProductRepository` |
| Domain events | `ProductCreatedEvent` |

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = default!;
    public decimal Price { get; private set; }

    private Product() { } // EF Core

    public Product(string name, decimal price)
    {
        Name = name;
        Price = price;
    }
}
```

### Application Layer

Orchestrates business use cases:

| Contains | Examples |
|----------|----------|
| Commands | `CreateProduct`, `UpdateProduct`, `DeleteProduct` |
| Queries | `GetAllProducts`, `GetProductById` |
| Handlers | `CreateProductHandler`, `GetAllProductsHandler` |
| DTOs | `ProductDto`, `CreateProductRequest` |
| Unit of Work | `IUnitOfWork` interface |
| Integration events | `ProductCreatedEvent` |

```csharp
public sealed record CreateProduct(string Name, decimal Price)
    : ICommand<ProductDto>;

public sealed class CreateProductHandler(ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<CreateProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(
        CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        unitOfWork.Products.Add(product);
        await unitOfWork.SaveChangesAsync(ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### Infrastructure Layer

Implements technical concerns:

| Contains | Examples |
|----------|----------|
| DbContext | `CatalogDbContext` |
| Repository impl | `ProductRepository` |
| Module composition | `CatalogModule` (registers services) |
| Design-time factory | `CatalogDbContextFactory` |

```csharp
public sealed class CatalogDbContext : ModuleDbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200);
        });
    }
}
```

### Presentation Layer

Exposes the module's API:

| Contains | Examples |
|----------|----------|
| Controllers | `ProductController` |
| Endpoints | Minimal API / REPR endpoints |
| Filters | Request/response filters |

```csharp
[ApiController]
[Route("api/products")]
public sealed class ProductController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetAll()
        => await _mediator.QueryAsync(new GetAllProducts());

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request)
        => await _mediator.SendAsync(new CreateProduct(request.Name, request.Price));
}
```

## Project References

```
Domain ────────────────────── no dependencies

Application ─── Domain ────── Modulus.Mediator, Modulus.Events

Infrastructure ─── Application ─── Domain ─── Modulus.EntityFrameworkCore, Modulus.Events

Presentation ─── Application ─── Shared.Presentation, Modulus.AspNetCore
```

## Dependency Rules

1. **Domain** depends on nothing
2. **Application** depends only on Domain
3. **Infrastructure** depends on Application and Domain
4. **Presentation** depends on Application (and optionally Domain)

Never reference Infrastructure from Presentation or Domain from Infrastructure.
