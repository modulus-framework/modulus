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

The host calls `AddMediator()` once (behaviors); each module contributes its
own handlers via `AddMediatorHandlers(...)` without re-registering behaviors.

## Commands

### Define a Command

```csharp
// Command with response
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

// Command without response
public sealed record DeleteProductCommand(Guid Id) : ICommand<Unit>;
```

### Implement a Handler

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

### Send a Command

```csharp
var id = await _mediator.SendAsync(new CreateProductCommand("Widget"));
```

## Queries

### Define a Query

```csharp
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;
```

### Implement a Handler

```csharp
public sealed class GetProductByIdHandler(IProductRepository repository)
    : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    public async Task<ProductDto?> HandleAsync(
        GetProductByIdQuery query,
        CancellationToken ct)
    {
        var product = await repository.GetByIdAsync(query.Id, ct);
        return product is null
            ? null
            : new ProductDto { Id = product.Id, Name = product.Name };
    }
}
```

### Execute a Query

```csharp
var product = await _mediator.QueryAsync(
    new GetProductByIdQuery(productId));
```

## Pipeline Behaviors

Behaviors run before/after every handler:

| Behavior | Purpose |
|----------|---------|
| `LoggingBehavior` | Logs command/query execution time |
| `ValidationBehavior` | Validates using FluentValidation |
| `TransactionBehavior` | Wraps the handler in a DB transaction (see below) |
| `FeatureGateBehavior` | Gates commands behind feature flags |
| `AuthorizationBehavior` | Checks permissions before execution |

### TransactionBehavior

- Wraps **every distinct** module `DbContext` (deduped by type), driven
  through EF's execution strategy so `EnableRetryOnFailure` providers work
  (keep handler bodies safe to re-run on transient-failure retry).
- **Skips queries** (`IQuery<T>`) and requests marked `[SkipTransaction]`.
- **Fail-fast on ambiguity**: with more than one context and no declared
  intent it throws — add `[Transactional(typeof(...))]`,
  `TransactionMode.AllContexts`, or `[SkipTransaction]`.
- Multi-context commits are **independent, non-atomic** — for cross-module
  consistency prefer the transactional [outbox](outbox).
- Deferred domain events drain after commit (via the transaction interceptor);
  manual transactions outside the pipeline are covered too.

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
public sealed class CreateProductHandler
    : ICommandHandler<CreateProductCommand, ErrorOr<Guid>>
{
    public async Task<ErrorOr<Guid>> HandleAsync(
        CreateProductCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(command.Name))
            return Error.Validation("Product.Name", "Name is required");

        // ... create product

        return product.Id;
    }
}
```

## Unit of Work Pattern

Each module defines its own non-generic `IUnitOfWork` and binds it to its own
`DbContext` in the module composition root:

```csharp
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}

// Module composition root
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());
```

Handlers call `CommitAsync` to persist:

```csharp
await unitOfWork.CommitAsync(ct);
```

The `TransactionBehavior` wraps the handler in a transaction automatically
(see above).
