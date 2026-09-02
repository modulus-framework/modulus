---
sidebar_position: 1
---

# Modulus Framework

![Modulus logo](/img/logo.png)

A modular-monolith framework for **.NET 10** that combines the simplicity of a single deployment with the boundaries of microservices.

## What is Modulus?

Modulus is a framework for building enterprise applications using a **modular-monolith** architecture. It provides:

- **Module system** with dependency management and lifecycle hooks
- **Clean Architecture** per module (Domain → Application → Infrastructure → Presentation)
- **CQRS** via a built-in mediator with pipeline behaviors
- **Event-driven** messaging with transactional outbox and inbox dedup
- **Multi-tenancy** with per-tenant data isolation
- **Identity** via OpenIddict with external IdP support
- **Production hardening** (rate limiting, health checks, idempotency, security headers, PII encryption)
- **CLI tool** for scaffolding apps, modules, and CRUD code

## Key Design Principles

| Principle | Description |
|-----------|-------------|
| **Modular boundaries** | Each business domain is an independent module with its own database, mediator handlers, and API surface |
| **Single deployment** | All modules run in a single process — no distributed systems complexity |
| **Clean Architecture** | Each module follows Domain → Application → Infrastructure → Presentation layers |
| **Convention over configuration** | Sensible defaults with escape hatches for customization |
| **Production-ready** | Built-in security, observability, and resilience patterns |

## Quick Example

```bash
# Install the CLI tool
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli

# Create a new application
modulus app MyApp

# Add a business module
modulus add-module Catalog

# Generate CRUD operations
modulus generate-crud Product --module Catalog

# Run the application
cd src/API/MyApp.Api
dotnet run
```

## Architecture at a Glance

```
┌─────────────────────────────────────────────────────────┐
│                      Host (API)                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │  Catalog     │  │  Orders     │  │  Inventory  │    │
│  │  Module      │  │  Module     │  │  Module     │    │
│  │ ┌─────────┐ │  │ ┌─────────┐ │  │ ┌─────────┐ │    │
│  │ │Domain   │ │  │ │Domain   │ │  │ │Domain   │ │    │
│  │ │App      │ │  │ │App      │ │  │ │App      │ │    │
│  │ │Infra    │ │  │ │Infra    │ │  │ │Infra    │ │    │
│  │ │Pres.    │ │  │ │Pres.    │ │  │ │Pres.    │ │    │
│  │ └─────────┘ │  │ └─────────┘ │  │ └─────────┘ │    │
│  └─────────────┘  └─────────────┘  └─────────────┘    │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │           Shared Kernel (Modulus.*)               │  │
│  │  Core · Data · Mediator · Events · Platform      │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

## Framework Highlights

- **Distributed Tracing** — W3C trace context flows across async boundaries and message brokers
- **Message Durability** — Publisher confirms (RabbitMQ), at-least-once semantics (Kafka)
- **Server-Side Projection** — Query directly for DTOs without materializing full entities
- **Event Assertions** — Built-in fakes and helpers for testing event-driven code
- **Architecture Rules** — Enforce module boundaries with integration event naming validation

## Next Steps

- [Features](features) — Explore the latest capabilities
- [Prerequisites](getting-started/prerequisites) — Set up your development environment
- [Quick Start](getting-started/quick-start) — Create your first Modulus app
- [Architecture](architecture/overview) — Understand the framework's design
