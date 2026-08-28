---
sidebar_position: 2
---

# Mediator

The mediator implements the CQRS pattern with command/query separation and pipeline behaviors.

## Setup

```csharp
// Host-level: register pipeline behaviors
builder.Services.AddMediator();

// Module-level: register handlers
services.AddMediatorHandlers(typeof(CatalogModule).Assembly);
```

## Commands

### Define a Command

```csharp
// Command with response
public sealed record CreateProduct(string Name, decimal Price)
    : ICommand<ProductDto>;

// Command without response
public sealed record DeleteProduct(Guid Id) : ICommand;
```

### Implement a Handler

```csharp
public sealed class CreateProductHandler(ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<CreateProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(
        CreateProduct command,
        CancellationToken ct = default)
    {
        var product = new Product(command.Name, command.Price);
        unitOfWork.Products.Add(product);
        await unitOfWork.SaveChangesAsync(ct);

        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### Send a Command

```csharp
var result = await _mediator.SendAsync(
    new CreateProduct("Widget", 9.99m));
```

## Queries

### Define a Query

```csharp
public sealed record GetProductById(Guid Id) : IQuery<ProductDto>;
```

### Implement a Handler

```csharp
public sealed class GetProductByIdHandler(IProductRepository repository)
    : IQueryHandler<GetProductById, ProductDto>
{
    public async Task<ProductDto> HandleAsync(
        GetProductById query,
        CancellationToken ct = default)
    {
        var product = await repository.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(Product), query.Id);

        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### Execute a Query

```csharp
var product = await _mediator.QueryAsync(
    new GetProductById(productId));
```

## Pipeline Behaviors

Behaviors run before/after every handler:

| Behavior | Purpose |
|----------|---------|
| `LoggingBehavior` | Logs command/query execution time |
| `ValidationBehavior` | Validates using FluentValidation |
| `TransactionBehavior` | Wraps handler in a database transaction |
| `FeatureGateBehavior` | Gates commands behind feature flags |
| `AuthorizationBehavior` | Checks permissions before execution |

### Custom Behavior

```csharp
public sealed class TimingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TimingBehavior<TRequest, TResponse>> _logger;

    public TimingBehavior(ILogger<TimingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        _logger.LogInformation(
            "{Request} completed in {Elapsed}ms",
            typeof(TRequest).Name,
            sw.ElapsedMilliseconds);

        return response;
    }
}
```

### Register a Behavior

```csharp
// Behaviors are registered globally by AddMediator()
// They apply to ALL commands and queries
```

## Error Handling

Handlers can throw exceptions or return `ErrorOr<T>`:

```csharp
public sealed class CreateProductHandler : ICommandHandler<CreateProduct, ErrorOr<ProductDto>>
{
    public async Task<ErrorOr<ProductDto>> HandleAsync(
        CreateProduct command,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(command.Name))
            return Error.Validation("Product.Name", "Name is required");

        // ... create product

        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

## Unit of Work Pattern

Each module defines its own IUnitOfWork:

```csharp
public interface ICatalogUnitOfWork : IUnitOfWork
{
    DbSet<Product> Products { get; }
}
```

Handlers call `SaveChangesAsync` to commit:

```csharp
await unitOfWork.SaveChangesAsync(ct);
```

The `TransactionBehavior` wraps this in a transaction automatically.
