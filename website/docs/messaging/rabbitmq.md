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

Binds from `"EventBus:RabbitMq"`:

```json
{
  "EventBus": {
    "RabbitMq": {
      "HostName": "localhost",
      "Port": 5672,
      "UserName": "guest",
      "Password": "guest",
      "VirtualHost": "/",
      "ExchangeName": "modulus.events",
      "QueueName": "modulus.events.app",
      "ExchangeType": "topic",
      "Durable": true,
      "AutoDelete": false,
      "PrefetchCount": 50,
      "AutoAck": false,
      "ReconnectDelayMs": 5000,
      "MaxDeliveryAttempts": 3,
      "DeadLetterExchange": null,
      "MessageTtlMs": null,
      "PublisherConfirms": true
    }
  }
}
```

## Usage

```csharp
// Module composition root
services.AddRabbitMqEventBus(config);
```

### Publishing Events

Publish integration events through the module bus (prefer the transactional
[outbox](outbox) so the publish survives crashes):

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
| **Topic exchange** | Route events by type (`modulus.events` by default) |
| **Publisher confirms** | On by default — a nacked/unroutable publish throws instead of silently losing events |
| **Manual acknowledgment** | `AutoAck: false` — ack only after the handler succeeds |
| **Prefetch control** | `PrefetchCount` limits concurrent messages (default 50) |
| **Poison handling** | Failures requeue with backoff up to `MaxDeliveryAttempts`, then nacked without requeue |
| **Dead letter exchange** | Opt-in via `DeadLetterExchange` — nacked messages route there instead of being dropped |
| **Unroutable publishes** | Logged + metered via `BasicReturn` handling |

## Exchange Topology

```
                     ┌──────────────────┐
                     │ modulus.events   │
                     │ (topic)          │
                     └────────┬─────────┘
                              │
               ┌──────────────┼──────────────┐
               │              │              │
     ┌─────────┴──────┐ ┌────┴─────┐ ┌─────┴────────┐
     │ queues bound   │ │...       │ │...           │
     │ by routing key │ │          │ │              │
     └────────────────┘ └──────────┘ └──────────────┘
```

## See Also

- [Kafka](kafka) — Alternative transport
- [Outbox](outbox) — Reliable publishing
