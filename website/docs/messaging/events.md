---
sidebar_position: 3
---

# Events

Modulus supports two types of events: domain events and integration events.

## Domain Events

Domain events represent things that happened within a module:

```csharp
public sealed record ProductCreatedEvent : IDomainEvent
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = default!;
    public decimal Price { get; init; }
}
```

### Publishing Domain Events

```csharp
public sealed class CreateProductHandler : ICommandHandler<CreateProduct, ProductDto>
{
    private readonly IDomainEventDispatcher _dispatcher;

    public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);

        // Domain event is dispatched after SaveChanges
        product.AddDomainEvent(new ProductCreatedEvent
        {
            ProductId = product.Id,
            Name = product.Name,
            Price = product.Price
        });

        await unitOfWork.SaveChangesAsync(ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

## Integration Events

Integration events are published to other modules or services:

```csharp
public sealed record ProductCreatedEvent : IIntegrationEvent
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = default!;
    public decimal Price { get; init; }
}
```

### Publishing Integration Events

Integration events are enqueued in the outbox during `SaveChangesAsync`:

```csharp
public sealed class CreateProductHandler : ICommandHandler<CreateProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        unitOfWork.Products.Add(product);

        // This automatically enqueues in the outbox
        product.AddIntegrationEvent(new ProductCreatedEvent
        {
            ProductId = product.Id,
            Name = product.Name,
            Price = product.Price
        });

        await unitOfWork.SaveChangesAsync(ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### Handling Integration Events

```csharp
public sealed class ProductCreatedHandler
    : IIntegrationEventHandler<ProductCreatedEvent>
{
    private readonly IInventoryService _inventory;

    public async Task HandleAsync(ProductCreatedEvent @event)
    {
        await _inventory.InitializeStockAsync(@event.ProductId);
    }
}
```

### Register Handlers

```csharp
// Module-level
services.AddIntegrationEventHandlers(typeof(OrdersModule).Assembly);
```

## Event Registry

Events are registered with stable names for serialization:

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddEvents(builder =>
    {
        builder.Register<ProductCreatedEvent>("catalog.product.created");
    });
}
```

## In-Process Bus

For same-process event dispatching:

```csharp
// Program.cs
builder.Services.AddModulusEvents();

// Publish
await _bus.PublishAsync(new ProductCreatedEvent { ... });
```

## See Also

- [Outbox](outbox) — Reliable event publishing
- [Inbox](inbox) — Idempotent consumption
- [RabbitMQ](rabbitmq) — External transport
- [Kafka](kafka) — External transport
