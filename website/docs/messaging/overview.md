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

## Setup

```csharp
// Program.cs
builder.Services.AddModulusEvents();       // Domain events + in-process bus
builder.Services.AddMediator();            // CQRS mediator
```

## See Also

- [Mediator](mediator) — CQRS command/query handling
- [Events](events) — Domain and integration events
- [Outbox](outbox) — Transactional outbox
- [Inbox](inbox) — Idempotent consumption
