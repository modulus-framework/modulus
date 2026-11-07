---
sidebar_position: 3
---

# Events

Modulus has two kinds of events. **Domain events** happen inside one module and are dispatched in the same process. **Integration events** cross module and service boundaries and are delivered through the module bus, the transactional outbox and an optional broker.

## Domain Events

A domain event implements `IDomainEvent`. An aggregate records one with `AddDomainEvent`, and the framework dispatches it to every `IDomainEventHandler<TEvent>` when the unit of work saves.

```csharp
public sealed record ProductCreatedDomainEvent(Guid ProductId, string Name) : IDomainEvent;

public sealed class Product : AggregateRoot
{
    public Product(string name, decimal price)
    {
        Name = name;
        Price = price;
        AddDomainEvent(new ProductCreatedDomainEvent(Id, name));
    }

    public string Name { get; private set; }
    public decimal Price { get; private set; }
}
```

Handlers run inside the save, so a failing domain handler fails the command. Dispatch isolates handlers from each other: one handler throwing does not stop the others, and the failures are reported together.

## Integration Events

An integration event is the public contract of a module. It is a record that derives from `IntegrationEventBase`, so its wire name follows from the owning module.

```csharp
// The marker names the owning module once.
public sealed class CatalogArea;

[IntegrationEvent<CatalogArea>]
public sealed record ProductCreated(Guid ProductId, string Name, decimal Price) : IntegrationEventBase;
```

The wire name is derived from the two type names, here `catalog.product-created.v1`. Renaming the event type changes the wire name, so the analyzer rule `MOD0003` and a contract snapshot test in generated apps catch accidental changes. `Version = 2` on the attribute starts a new version when the contract changes.

### Publishing

Integration events are published through `IModuleBus`. The default bus runs in-process. A broker bus replaces it when registered after `AddModulusEvents`.

```csharp
await moduleBus.PublishAsync(new ProductCreated(product.Id, product.Name, product.Price), ct);
```

Prefer publishing through the outbox. When an integration event is raised in a `ModuleDbContext`, the context writes it to the outbox table in the same database transaction as the change, so the event is not lost if the process crashes. See [Outbox](./outbox).

### Handling

```csharp
public sealed class ProductCreatedHandler : IIntegrationEventHandler<ProductCreated>
{
    private readonly IInventoryService _inventory;

    public ProductCreatedHandler(IInventoryService inventory) => _inventory = inventory;

    public Task HandleAsync(ProductCreated @event, CancellationToken ct) =>
        _inventory.InitializeStockAsync(@event.ProductId, ct);
}
```

Handlers are delivered at least once. Wrap them with the [inbox](./inbox) so a redelivered event is processed only once.

## Registration

`AddModulusEvents` scans the given assemblies. It registers the domain and integration event handlers it finds, and records the integration event types for the registry.

```csharp
// Program.cs (or a module's ConfigureServices)
services.AddModulusEvents(typeof(CatalogArea).Assembly, typeof(OrdersArea).Assembly);

// Optional: replace the in-process bus with a broker
services.AddRabbitMqEventBus();    // or services.AddKafkaEventBus();
```

Call `AddModulusEvents` once for the host and let each module contribute its own assembly. The registry is shared, so every module's events are known to the broker bindings.

## See Also

- [Outbox](./outbox): reliable publishing
- [Inbox](./inbox): idempotent consumption
- [RabbitMQ](./rabbitmq) and [Kafka](./kafka): external transports
- [Webhooks](./webhooks): deliver integration events to external endpoints
- [Sagas](./sagas): long-running processes that react to events
