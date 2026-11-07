---
sidebar_position: 1
---

# Modulus Framework

![Modulus logo](/img/logo.png)

A modular-monolith framework for **.NET 8 and .NET 10** that combines the simplicity of a single deployment with the boundaries of microservices.

## What is Modulus?

Modulus is a framework for building enterprise applications using a **modular-monolith** architecture. It provides:

- **Module system** with dependency management and lifecycle hooks
- **Clean Architecture** per module (Domain → Application → Infrastructure → Presentation)
- **CQRS** via a built-in mediator with pipeline behaviors
- **Event-driven** messaging with transactional outbox and inbox dedup
- **Multi-tenancy** with per-tenant data isolation; a company is a tenant, with membership checks and database isolation ([Security Model](./security/overview))
- **Identity** via OpenIddict with external IdP support (OIDC, LDAP/AD, SAML, passkeys, SCIM)
- **Service edges**: per-client [backends for frontends](./platform/bff), [gRPC](./platform/grpc), [GraphQL](./platform/graphql), [realtime](./platform/realtime) push, and signed [webhooks](./messaging/webhooks)
- **Production hardening** (rate limiting, health checks, idempotency, security headers, PII encryption, a startup security guard)
- **Analytics**: versioned, masked datasets sent to ClickHouse, Parquet, Doris or the module bus ([Analytics](./analytics/overview))
- **CLI tool** for scaffolding apps, modules, CRUD code and optional features
- **[Modulus UI](./ui/overview)**: an admin-panel layer for Blazor, MVC, Razor Pages and Liquid templates, with one shell, menu and theme. It ships as separate `Synvro.Modulus.Ui.*` and `Synvro.Modulus.*` UI packages and works with any Modulus app that has a web project

## Status

The current release is **2.0.0**, published on NuGet as the `Synvro.Modulus.*` packages. The breaking changes from 1.8.x are listed in the [changelog](https://github.com/modulus-framework/modulus/blob/main/CHANGELOG.md). The docs describe the 2.0.0 line.

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
dotnet tool install -g --add-source ./nupkg Synvro.Modulus.Cli

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
