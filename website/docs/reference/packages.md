# NuGet packages

Every package the framework publishes, with its purpose. All packages share one version.

Install a package with `dotnet add package <id>`; the CLI adds the ones an app needs (see the `modulus add-*` commands).

| Package | Purpose |
|---|---|
| `Synvro.Modulus.AI.Connector` | Hosts an AI platform's read-only connector for a Modulus app. |
| `Synvro.Modulus.AI.Connector.EntityFrameworkCore` | EF Core storage for the Modulus AI connector (change journal and change feed). |
| `Synvro.Modulus.AI.Connector.Testing` | Conformance tests for the Modulus AI connector. |
| `Synvro.Modulus.Analytics` | Analytics Integration Layer contracts for Modulus. |
| `Synvro.Modulus.Analytics.Airflow` | Apache Airflow trigger for Modulus analytics. |
| `Synvro.Modulus.Analytics.Apicurio` | Apicurio Registry publisher for Modulus analytics schemas. |
| `Synvro.Modulus.Analytics.ClickHouse` | ClickHouse sink for Modulus analytics. |
| `Synvro.Modulus.Analytics.Debezium` | Debezium change reader for Modulus analytics. |
| `Synvro.Modulus.Analytics.Doris` | Apache Doris sink for Modulus analytics. |
| `Synvro.Modulus.Analytics.DuckDB` | DuckDB reconciliation reader for Modulus analytics. |
| `Synvro.Modulus.Analytics.EntityFrameworkCore` | EF Core durable delivery and backfill for Modulus analytics. |
| `Synvro.Modulus.Analytics.Management` | Operations API for Modulus analytics delivery. |
| `Synvro.Modulus.Analytics.Messaging` | Messaging sink for Modulus analytics (publishes records as integration events). |
| `Synvro.Modulus.Analytics.OpenLineage` | OpenLineage emitter for Modulus analytics backfills. |
| `Synvro.Modulus.Analytics.OpenMetadata` | OpenMetadata publisher for Modulus analytics datasets. |
| `Synvro.Modulus.Analytics.Parquet` | Parquet sink for Modulus analytics. |
| `Synvro.Modulus.Analytics.Superset` | Apache Superset guest tokens for Modulus analytics. |
| `Synvro.Modulus.Analytics.Testing` | Test kit for Modulus analytics sinks. |
| `Synvro.Modulus.AspNetCore` | ASP.NET Core integration for Modulus. |
| `Synvro.Modulus.AspNetCore.Redis` | Redis idempotency store for Modulus HTTP requests. |
| `Synvro.Modulus.AuditLogging.EntityFrameworkCore` | EF Core audit log storage for Modulus. |
| `Synvro.Modulus.Authorization.EntityFrameworkCore` | EF Core permission grant store for Modulus authorization. |
| `Synvro.Modulus.Authorization.Management` | Administration API for Modulus authorization. |
| `Synvro.Modulus.BackgroundJobs.Quartz` | Quartz.NET background job scheduling for Modulus. |
| `Synvro.Modulus.Bff` | Backends for Frontends (web, mobile, partner) for Modulus. |
| `Synvro.Modulus.Caching.Redis` | Redis caching for Modulus. |
| `Synvro.Modulus.Cli` | Command-line tool to scaffold Modulus apps, modules and CRUD code. |
| `Synvro.Modulus.Core` | Core abstractions and utilities for the Modulus framework. |
| `Synvro.Modulus.Data.Abstractions` | Data access abstractions for Modulus (repository and unit of work). |
| `Synvro.Modulus.Data.MongoDB` | MongoDB data access provider for Modulus. |
| `Synvro.Modulus.Data.MySQL` | MySQL provider for Modulus EF Core modules. |
| `Synvro.Modulus.Data.PostgreSQL` | PostgreSQL provider for Modulus EF Core modules. |
| `Synvro.Modulus.Data.SQLite` | SQLite provider for Modulus EF Core modules. |
| `Synvro.Modulus.Data.SqlServer` | SQL Server provider for Modulus EF Core modules. |
| `Synvro.Modulus.EntityFrameworkCore` | Entity Framework Core integration for Modulus. |
| `Synvro.Modulus.EventBus.Kafka` | Kafka transport for Modulus integration events. |
| `Synvro.Modulus.EventBus.RabbitMQ` | RabbitMQ transport for Modulus integration events. |
| `Synvro.Modulus.Events` | In-process eventing for Modulus. |
| `Synvro.Modulus.GraphQL` | GraphQL endpoint for Modulus (GraphQL.NET). |
| `Synvro.Modulus.Grpc` | gRPC services and clients for Modulus. |
| `Synvro.Modulus.Identity` | OpenIddict identity and authorization server for Modulus. |
| `Synvro.Modulus.Identity.IntegrationEvents` | Integration events published by the Modulus Identity module. |
| `Synvro.Modulus.Identity.Messaging` | Email and SMS providers for Modulus identity. |
| `Synvro.Modulus.Inbox` | Idempotent integration event consumption for Modulus (EF Core). |
| `Synvro.Modulus.Inbox.MongoDB` | MongoDB inbox store for Modulus. |
| `Synvro.Modulus.Localization.EntityFrameworkCore` | EF Core translation store for Modulus localization. |
| `Synvro.Modulus.Media` | Media library for Modulus (validated uploads). |
| `Synvro.Modulus.Media.EntityFrameworkCore` | EF Core store for the Modulus media library. |
| `Synvro.Modulus.Mediator` | In-process mediator for Modulus (commands, queries, pipeline behaviours). |
| `Synvro.Modulus.Messaging` | Consumers, publish and send for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Inbox.EntityFrameworkCore` | EF Core inbox for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Inbox.Npgsql` | Npgsql inbox for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Management` | Operator API for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Modulus` | Modulus host adapter for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Outbox.EntityFrameworkCore` | EF Core transactional outbox for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Outbox.Npgsql` | Npgsql transactional outbox for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Sagas` | State machine sagas for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Sagas.EntityFrameworkCore` | EF Core saga repository for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Sagas.PostgreSql` | PostgreSQL saga repository for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Testing` | Test harness for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Transports.InMemory` | In-memory transport for Modulus.Messaging (tests and single process). |
| `Synvro.Modulus.Messaging.Transports.PostgreSql` | PostgreSQL transport for Modulus.Messaging. |
| `Synvro.Modulus.Messaging.Transports.RabbitMQ` | RabbitMQ transport for Modulus.Messaging. |
| `Synvro.Modulus.MultiTenancy.EntityFrameworkCore` | EF Core tenant store for Modulus multi-tenancy. |
| `Synvro.Modulus.Notifications.EntityFrameworkCore` | EF Core stores for Modulus notifications. |
| `Synvro.Modulus.Observability` | OpenTelemetry wiring and diagnostics for Modulus. |
| `Synvro.Modulus.Outbox` | Transactional outbox for Modulus (EF Core). |
| `Synvro.Modulus.Outbox.Abstractions` | Shared contracts for the Modulus transactional outbox. |
| `Synvro.Modulus.Outbox.Management` | Operations API for the Modulus transactional outbox. |
| `Synvro.Modulus.Outbox.MongoDB` | MongoDB transactional outbox for Modulus. |
| `Synvro.Modulus.Platform` | Platform services for Modulus (tenancy, authorization, jobs, caching, storage). |
| `Synvro.Modulus.Realtime` | Realtime push of integration events to clients (SSE and SignalR). |
| `Synvro.Modulus.Realtime.Redis` | Redis backplane for Modulus realtime. |
| `Synvro.Modulus.Sagas` | Rebus-based sagas for Modulus. |
| `Synvro.Modulus.Search` | Full-text search for Modulus (OpenSearch and Solr). |
| `Synvro.Modulus.Secrets.AzureKeyVault` | Azure Key Vault secret provider for Modulus. |
| `Synvro.Modulus.Secrets.OpenBao` | OpenBao secret provider for Modulus. |
| `Synvro.Modulus.SignalR.Backplane` | SignalR scale-out backplane options for Modulus. |
| `Synvro.Modulus.Storage.AzureBlobs` | Azure Blob Storage provider for Modulus. |
| `Synvro.Modulus.Storage.GoogleCloud` | Google Cloud Storage provider for Modulus. |
| `Synvro.Modulus.Storage.S3` | Amazon S3 storage provider for Modulus. |
| `Synvro.Modulus.Tenancy.Lifecycle` | Tenant data lifecycle (export, offboarding, deletion) for Modulus. |
| `Synvro.Modulus.Tenancy.Lifecycle.EntityFrameworkCore` | EF Core store for the Modulus tenant data lifecycle. |
| `Synvro.Modulus.Testing` | Integration-test harness for Modulus applications. |
| `Synvro.Modulus.Testing.Architecture` | Architecture rules for Modulus modular monoliths. |
| `Synvro.Modulus.Testing.Authorization` | Conformance tests for Modulus permission grants. |
| `Synvro.Modulus.Webhooks` | Signed outgoing webhooks for Modulus integration events. |
