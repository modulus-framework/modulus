---
sidebar_position: 1
---

# Messaging Overview

Modulus provides a complete messaging stack for event-driven architecture within the monolith.

## Components

| Component | Package | Purpose |
|-----------|---------|---------|
| **Mediator** | `Modulus.Mediator` | In-process CQRS (commands/queries) |
| **Events** | `Modulus.Events` | Domain and integration events |
| **Outbox** | `Modulus.Outbox` | Transactional outbox for reliable publishing |
| **Inbox** | `Modulus.Inbox` | Idempotent message consumption |
| **RabbitMQ** | `Modulus.EventBus.RabbitMQ` | RabbitMQ transport |
| **Kafka** | `Modulus.EventBus.Kafka` | Kafka transport |
| **Sagas** | `Modulus.Sagas` | Rebus-based saga orchestration |

## Message Flow

```
┌─────────────────────────────────────────────────────────────┐
│                     Command/Query Flow                       │
│                                                              │
│  Controller ──→ IMediator ──→ Pipeline ──→ Handler ──→ Repo │
│                    │               │                          │
│                    │          ┌────┴────┐                    │
│                    │          │Logging  │                    │
│                    │          │Validation│                   │
│                    │          │Transaction│                  │
│                    │          │FeatureGate│                  │
│                    │          └─────────┘                    │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                   Integration Event Flow                     │
│                                                              │
│  Handler ──→ SaveChanges ──→ Outbox ──→ Processor ──→ Bus   │
│                                         │                    │
│                                    ┌────┴────┐              │
│                                    │ Claim   │              │
│                                    │ Dispatch│              │
│                                    │ Backoff │              │
│                                    └─────────┘              │
│                                                              │
│  Bus ──→ Consumer ──→ Inbox ──→ Handler                      │
│               │           │                                  │
│          ┌────┴────┐ ┌────┴────┐                            │
│          │ Idempotent│ │ Dedup  │                            │
│          └─────────┘ └─────────┘                            │
└─────────────────────────────────────────────────────────────┘
```

## In-Process vs External

| Aspect | In-Process | External (RabbitMQ/Kafka) |
|--------|------------|---------------------------|
| **Scope** | Same monolith | Cross-service |
| **Latency** | Microseconds | Milliseconds |
| **Ordering** | Guaranteed | Best-effort (Kafka: per-partition) |
| **Durability** | In-memory | Persistent |
| **Use case** | Module-to-module | Service-to-service |

## Trace Context Propagation

W3C `TraceParent` and `TraceState` flow across all message boundaries:

- **Integration event envelopes** — Carry trace context
- **Outbox messages** — Store parent trace for replay
- **RabbitMQ headers** — TraceParent/TraceState in message headers
- **Kafka headers** — TraceParent/TraceState alongside envelope
- **Consumer restoration** — Automatically restores parent activity

```csharp
// Producer
using var activity = new ActivitySource("MyApp").StartActivity("ProcessOrder");
await bus.PublishAsync(new OrderCreatedEvent { ... });
// TraceId automatically captured in envelope

// Consumer (automatic)
// Receives envelope with TraceParent/TraceState
// Restores activity with same TraceId
```

## Message Durability

**RabbitMQ:**
- Publisher confirms ensure broker persistence
- Persistent delivery mode survives broker restarts
- Auto-recovery on connection loss
- Connection pooling + resilience

**Kafka:**
- At-least-once semantics (EnableAutoCommit = false)
- Configurable partition key distribution (`IPartitionKeyProvider`)
- Per-aggregate-id ordering via partition key

## Causation Tracking

Track event chains across service boundaries:

```csharp
// Consumed event automatically sets ambient causation ID
// New events published in response stamp the chain
await eventBus.PublishAsync(new OrderFulfilled { ... });
// CausationId traces back to original OrderCreatedEvent
```

Access via `ICausationIdContext` for custom event correlation.

## Setup

```csharp
// Program.cs
builder.Services.AddModulusEvents();       // Domain events + in-process bus
builder.Services.AddMediator();            // CQRS mediator
builder.Services.AddRabbitMqEventBus(config); // or AddKafkaEventBus
builder.Services.AddModulusOutbox();       // Transactional outbox
```

## See Also

- [Mediator](mediator) — CQRS command/query handling
- [Events](events) — Domain and integration events
- [Outbox](outbox) — Transactional outbox with trace context
- [Inbox](inbox) — Idempotent consumption
- [RabbitMQ](rabbitmq) — Broker configuration
- [Kafka](kafka) — Kafka partition strategy
