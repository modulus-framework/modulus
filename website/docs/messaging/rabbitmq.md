---
sidebar_position: 6
---

# RabbitMQ

Modulus integrates with RabbitMQ for cross-service event delivery.

## Setup

```bash
modulus app MyApp --message-broker rabbitmq
```

## Configuration

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "ExchangeName": "modulus"
  }
}
```

## Usage

```csharp
// Module composition root
services.AddRabbitMqEventBus(config);
```

### Publishing Events

```csharp
public sealed class ProductCreatedHandler : ICommandHandler<CreateProduct, ProductDto>
{
    private readonly IModuleBus _bus;

    public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        await _unitOfWork.SaveChangesAsync(ct);

        await _bus.PublishAsync(new ProductCreatedEvent
        {
            ProductId = product.Id,
            Name = product.Name
        });

        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### Consuming Events

```csharp
public sealed class ProductCreatedConsumer
    : IIntegrationEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent @event)
    {
        // Process the event
    }
}
```

## Features

| Feature | Description |
|---------|-------------|
| **Topic exchange** | Route events by type |
| **Auto-reconnect** | Handles connection failures |
| **Prefetch control** | Limit concurrent messages |
| **Manual acknowledgment** | Ensure processing before ack |
| **Dead letter queue** | Route failed messages |

## Exchange Topology

```
                    ┌──────────────────┐
                    │  modulus-exchange │
                    │  (topic)         │
                    └────────┬─────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
    ┌─────────┴──────┐ ┌────┴─────┐ ┌─────┴────────┐
    │ catalog.events │ │orders.events│ │inventory.events│
    │ (queue)        │ │ (queue)    │ │ (queue)       │
    └────────────────┘ └──────────┘ └──────────────┘
```

## See Also

- [Kafka](kafka) — Alternative transport
- [Outbox](outbox) — Reliable publishing
