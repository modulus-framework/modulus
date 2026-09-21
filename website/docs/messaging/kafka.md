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

Binds from `"EventBus:Kafka"`:

```json
{
  "EventBus": {
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "GroupId": "modulus-consumer",
      "AutoOffsetReset": "Earliest",
      "TopicPrefix": "modulus",
      "Acks": "all",
      "EnableDlq": true,
      "DeadLetterTopicSuffix": ".dlq",
      "MaxDeliveryAttempts": 5,
      "SaslMechanism": "Plain",
      "SecurityProtocol": "Plaintext",
      "SaslUsername": null,
      "SaslPassword": null
    }
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
await _bus.PublishAsync(new ProductCreatedIntegrationEvent(product.Id));
```

### Consuming Events

```csharp
public sealed class ProductCreatedConsumer
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent @event)
    {
        // Process the event (must be idempotent — delivery is at-least-once)
    }
}
```

## Features

| Feature | Description |
|---------|-------------|
| **Idempotent producer** | Enabled exactly when `Acks` is `"all"`; weaker settings are at-most-once |
| **Consumer groups** | Horizontal scaling (`EnableAutoCommit = false`, at-least-once) |
| **Partitioning** | Order per key |
| **Dead-letter topic** | Poison/malformed messages go to `{topic}.dlq` when `EnableDlq` (default on); otherwise committed past |
| **Poison handling** | Failed offsets are seeked-back for genuine redelivery, up to `MaxDeliveryAttempts` |

## Topic Naming

```
{TopicPrefix}.{Type.FullName}

Example (prefix "modulus"):
modulus.MyApp.Modules.Catalog.Application.IntegrationEvents.ProductCreatedIntegrationEvent
```

## See Also

- [RabbitMQ](rabbitmq) — Alternative transport
- [Outbox](outbox) — Reliable publishing
