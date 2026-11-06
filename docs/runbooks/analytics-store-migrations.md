# Analytics store migrations

This runbook covers the EF Core tables the analytics layer needs, and the one change to an existing table (the outbox `SchemaVersion` column).

## 1. The analytics store (new tables)

The analytics delivery store is an EF Core context. It is not owned by a module, so the application owns its migrations:

1. Derive the application's context from `ModulusAnalyticsDbContext` (the base class has no tenant filter, because the delivery worker serves every tenant).
2. Register it with `AddModulusAnalyticsStore<TContext>(configuration)` next to `AddModulusAnalytics(...)`.
3. Generate and apply the migration.

The sample in `samples/Modulus.Analytics.MigrationSample` does steps 1 and 3. Generate the migration:

```bash
dotnet ef migrations add InitialAnalyticsStore \
  --project samples/Modulus.Analytics.MigrationSample/Modulus.Analytics.MigrationSample.csproj \
  --output-dir Migrations
```

Review the SQL before it reaches a database. The sample's output is in `samples/Modulus.Analytics.MigrationSample/sql-initial-analytics-store.sql`, generated with `dotnet ef migrations script --idempotent`:

```bash
dotnet ef migrations script --idempotent \
  --project samples/Modulus.Analytics.MigrationSample/Modulus.Analytics.MigrationSample.csproj \
  --output analytics-store.sql
```

The script creates `analytics_deliveries` (the queue) and `analytics_backfill_runs` (backfill and incremental runs), with their indexes. It is idempotent, so a DBA can run it on any environment.

The migration was verified by applying the generated script to a fresh PostgreSQL 16.15 database, which created both tables and recorded the migration in `__EFMigrationsHistory`. The EF integration suite (`Modulus.Analytics.EntityFrameworkCore.Integration.Tests`, 8 tests) passes against that server.

## 2. The outbox `SchemaVersion` column (existing tables)

Every outbox row now stores the contract version of its event (the `Version` in `[IntegrationEvent<TModule>(Version = N)]`). Events that declare no version, or use the legacy `IntegrationEventNameAttribute`, store `NULL`.

This adds a nullable `SchemaVersion` integer column to each module's `outbox_messages` table (with the module's table prefix, for example `cat_outbox_messages`). Each module's context picks it up on its next `dotnet ef migrations add`. The migration is:

- additive and nullable: on PostgreSQL, adding a nullable column without a default is a metadata-only change and does not rewrite rows;
- backward compatible for readers: existing rows keep `NULL`, and nothing reads the column to route a message;
- **apply it before deploying the new code.** Once the code writes `SchemaVersion`, an insert into a table without the column fails.

Example for PostgreSQL, when the migration is written by hand instead of generated:

```sql
ALTER TABLE cat_outbox_messages ADD COLUMN IF NOT EXISTS "SchemaVersion" integer NULL;
```

Replace `cat_` with the module's prefix. A downstream consumer that replays outbox rows can read `SchemaVersion` to choose the right payload reader, without parsing the payload.

## 3. Verification

- `OutboxSchemaVersionTests` (4 tests): a versioned event stores its version, an unversioned event stores 1, a legacy-named event stores `NULL`.
- The migration sample builds with `dotnet build` and generates the migration above with `dotnet ef`.

## Running the PostgreSQL integration tests without Docker

Set `MODULUS_TEST_POSTGRES_CONNECTION` to a superuser connection string for an existing PostgreSQL server. The fixture then creates a fresh database per test on that server and skips the container. Without the variable it starts `postgres:16-alpine` through Testcontainers, as before.

```bash
export MODULUS_TEST_POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=change-me;Database=postgres"
dotnet test tests/integration/Modulus.Analytics.EntityFrameworkCore.Integration.Tests
```
