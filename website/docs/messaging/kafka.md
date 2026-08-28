---
sidebar_position: 7
---

# Kafka

Modulus integrates with Apache Kafka for high-throughput event streaming.

## Setup

```bash
modulus app MyApp --message-broker kafka
```

## Configuration

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "modulus",
    "AutoOffsetReset": "Earliest"
  }
}
```

## Usage

```csharp
// Module composition root
services.AddKafkaEventBus(config);
```

### Publishing Events

```csharp
await _bus.PublishAsync(new ProductCreatedEvent
{
    ProductId = product.Id,
    Name = product.Name
});
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
| **Idempotent producer** | Avoids duplicate messages |
| **Consumer groups** | Horizontal scaling |
| **Partitioning** | Order per key |
| **Offset management** | Manual or auto commit |

## Topic Naming

```
{module}.{event-type}

Examples:
catalog.product.created
orders.order.placed
inventory.stock.updated
```

## See Also

- [RabbitMQ](rabbitmq) — Alternative transport
- [Outbox](outbox) — Reliable publishing
