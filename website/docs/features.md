---
sidebar_position: 2
---

# Framework Features

Modulus is a set of opt-in packages. Each feature below is one package or one registration, so an application adds only what it needs. Every page links to the details.

## Architecture and modules

- **Explicit module registration.** `AddModulus` takes the modules in order, and that order is used for every lifecycle phase. There is no reflection-based discovery. See [Module system](./architecture/module-system).
- **Clean Architecture per module:** Domain, Application, Infrastructure and Presentation. See [Clean architecture](./architecture/clean-architecture).
- **Per-module database and unit of work.** Each module owns its `DbContext`, its migrations and its `IUnitOfWork`. See [Entity Framework](./data/entity-framework).

## Mediator and domain model

- **CQRS mediator** with `ICommand<TResult>` and `IQuery<TResult>`, pipeline behaviours (validation, logging, transactions, caching, audit) and no MediatR dependency. See [Mediator](./messaging/mediator).
- **Domain model** with `AggregateRoot`, domain events and specifications. See [Events](./messaging/events).

## Messaging

- **Transactional outbox.** Integration events are written in the same database transaction as the change. A background processor dispatches them with retries and dead-letters. See [Outbox](./messaging/outbox).
- **Inbox.** Each handler runs once per event id, even when the broker redelivers. See [Inbox](./messaging/inbox).
- **Brokers.** RabbitMQ and Kafka, both through `IModuleBus`. See [RabbitMQ](./messaging/rabbitmq) and [Kafka](./messaging/kafka).
- **Sagas.** Long-running processes on Rebus. See [Sagas](./messaging/sagas).
- **Webhooks.** Signed delivery of integration events to external endpoints. See [Webhooks](./messaging/webhooks).
- **Realtime push.** Server-Sent Events by default, SignalR as an option. See [Realtime](./platform/realtime).

## Data

- **Relational:** SQL Server, PostgreSQL, MySQL and SQLite through Entity Framework Core.
- **Document:** MongoDB, with tenant-scoped collections. See [MongoDB](./data/mongodb).
- **Migrations:** one set per module. See [Migrations](./data/migrations).

## Multi-tenancy and security

- **Companies as tenants.** Membership checks, suspension and closure of companies, and database isolation (row-level security or a database per tenant). See [Security model](./security/overview).
- **Closed by default.** Every endpoint needs a signed-in user unless it is explicitly opened. A startup guard refuses unprotected endpoints.
- **Authorization.** Permissions, scoped grants, approval limits, approval workflows, access requests and step-up sign-in. See [Authorization](./platform/authorization).
- **Audit.** A hash-chained security audit per company, with a database store. See [Security model](./security/overview).

## Identity

- **OpenIddict** token server with password, client-credentials, refresh, token-exchange, device-code and passkey grants.
- **External identity:** OIDC adapters for Auth0, Keycloak, Azure AD, Okta, Duende and Authentik, plus LDAP and Active Directory, SAML 2.0, and SCIM 2.0 provisioning.
- **Sessions:** sign-ins are tracked, can be capped, and can be ended from the server. See [Identity](./identity/overview).

## Service edges

- **Backends for frontends.** One gateway per client type (web, mobile and partner) with its own sign-in and edge rules. See [BFF](./platform/bff).
- **gRPC** with status mapping, health checks and resilient clients. See [gRPC](./platform/grpc).
- **GraphQL** with one schema built from module contributors and resolvers that go through the mediator. See [GraphQL](./platform/graphql).

## Analytics

- **Versioned datasets** with masking, consent, plan gating, quality checks and retention. Sinks for ClickHouse, Parquet, Apache Doris and the module bus. See [Analytics](./analytics/overview).

## Platform services

- **Caching** in memory, as a FusionCache hybrid, or with Redis. See [Caching](./platform/caching).
- **Background jobs** in process, or durable and clustered with Quartz. See [Background jobs](./platform/background-jobs).
- **File storage** on local disk, S3 or Azure Blobs. See [Storage](./platform/storage).
- **Media, localization, notifications, OpenBao secrets and tenant data lifecycle.** See [Platform add-ons](./platform/add-ons).

## Production hardening

- Rate limiting, API versioning, health probes, CORS, security headers, idempotency keys, correlation ids, feature flags, OpenAPI, and a secrets guard that refuses committed credentials.
- Encryption of personal data at rest. See the [hardening pages](./hardening/rate-limiting).

## Observability

- OpenTelemetry tracing and metrics across the mediator, outbox, caches and database commands. See [Observability](./observability/overview).

## Tooling

- **`modulus` CLI** for apps, modules, CRUD code, migrations, BFFs, gRPC, GraphQL, webhooks, realtime, AI, analytics and audit stores. See the [CLI reference](./cli/).
- **Analyzers** for anonymous endpoints (`MOD0001`), classified data in logs (`MOD0002`) and event names (`MOD0003`).
- **Testing** with a web-application factory, architecture rules and a security probe suite. See [Testing](./testing/overview).
