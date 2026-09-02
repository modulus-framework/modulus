---
sidebar_position: 5
---

# Migrations

Modulus supports two migration engines: EF Core (default) and dbsh (SQL-first).

## EF Core Migrations (Default)

### Create a Migration

```bash
modulus migrate add InitialCreate
```

This scaffolds a migration in each module's `Infrastructure/Migrations/` folder.

### Apply Migrations

```bash
modulus migrate update
```

### Per-Module Migration

```bash
modulus migrate add InitialCreate --module Catalog
modulus migrate update --module Catalog
```

### How It Works

1. `dotnet ef` runs in each module's Infrastructure project
2. Generates migration files in `Infrastructure/Migrations/`
3. `MigrateModulusDatabasesAsync()` applies them at startup

### Startup Behavior

```csharp
// Program.cs
await app.Services.MigrateModulusDatabasesAsync();
```

| Mode | Behavior |
|------|----------|
| `MigrateOrCreate` (default) | `Migrate()` when migrations exist, else `EnsureCreated()` |
| `Migrate` | Always applies migrations; throws if none exist |
| `EnsureCreated` | Snapshot only; no migration history |

### Design-Time Factory

Each module has a `DbContextFactory` for `dotnet ef`:

```csharp
public sealed class CatalogDbContextFactory
    : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseSqlite("Data Source=catalog.db");
        return new CatalogDbContext(optionsBuilder.Options);
    }
}
```

Connection string resolution:

1. `{MODULE}_CONNECTION` environment variable
2. Design-time default from factory

## dbsh Migrations (SQL-First)

dbsh is an optional migration engine that lets you author plain `.sql` files
instead of EF Core's C#-based model diffing. Each module that uses dbsh manages
its own SQL scripts under `Infrastructure/Database/Migrations/`. The
`dbsh` .NET global tool handles validation, sequencing, and tracking.

### When to Use

- You prefer writing SQL migrations by hand
- You need provider-specific SQL that EF Core's diffing can't produce
- You want schema changes versioned as plain text

### Setup

```bash
modulus app MyApp --migration-engine dbsh
```

Or when adding a module to an existing app:

```bash
modulus add-module Orders --migration-engine dbsh
```

If `--migration-engine` is omitted, the CLI defaults to `dbsh` when every
existing module already uses dbsh, otherwise `efcore`.

### Structure

A dbsh module's Infrastructure project has this layout:

```
Catalog.Infrastructure/
├── Database/
│   ├── Config/
│   │   └── migration.json          # dbsh config (provider + connection)
│   └── Migrations/
│       └── Catalog/
│           └── 001_AddProducts.sql  # your SQL scripts
├── Migrations/                      # (empty, no EF files)
├── {Module}DbContext.cs
├── {Module}DbContextFactory.cs
└── {Module}Module.cs               # composition root
```

### Configuration

The CLI generates a per-module `Database/Config/migration.json`:

```json
{
  "migration": {
    "version": "1.0.0",
    "database": {
      "provider": "sqlite",
      "connectionString": "${CATALOG_CONNECTION}"
    },
    "scripts": {
      "path": "./Database/Migrations",
      "pattern": "*.sql"
    },
    "execution": {
      "lockTimeoutSeconds": 300,
      "commandTimeoutSeconds": 3600,
      "batchSize": 10,
      "stopOnFailure": true
    }
  }
}
```

Connection strings support `${ENV_VAR}` placeholders. Set the env var at
deploy time:

```bash
export CATALOG_CONNECTION="Data Source=catalog.db"
```

The CLI also generates a `Database/Config/local.json` environment override
(empty by default — dbsh merges it on top of `migration.json`).

### Writing Migrations

SQL files live under `Database/Migrations/{ModuleName}/{Seq}_{Name}.sql`.
The CLI scaffolds the first migration:

```bash
modulus migrate add AddProductsTable --module Catalog
```

This creates a migration stub at `Database/Migrations/Catalog/001_AddProducts.sql`:

```sql
-- Migration: 001_AddProductsTable
-- Module: Catalog

CREATE TABLE Products (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Price REAL NOT NULL
);
```

For down-migrations, create a companion file in the same directory with
a `.down.sql` suffix:

```sql
-- Migration: 001_AddProductsTable.down
-- Module: Catalog

DROP TABLE Products;
```

### Applying Migrations

```bash
modulus migrate update
modulus migrate update --module Catalog
```

The CLI detects dbsh modules by checking for
`Database/Config/migration.json` and runs:

1. `dbsh init` (idempotent — creates tracking tables if missing)
2. `dbsh migrate --yes` (applies pending `.sql` files in sequence)

On a fresh database, `dbsh init` must run before `dbsh migrate`; the CLI
handles this automatically.

### Framework Integration

dbsh modules register their context as externally managed:

```csharp
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlite(configuration.GetConnectionString("Catalog")
        ?? "Data Source=catalog.db"))
    .ExternallyManaged<CatalogDbContext>();
```

`ExternallyManaged<TContext>` tells `MigrateModulusDatabasesAsync` to skip
the context — the schema is applied by `dbsh`, not by EF Core. The startup
logs *"Skipping CatalogDbContext (schema managed externally)."* for these
contexts.

### CI/CD

Set `{MODULE}_CONNECTION` in your pipeline:

```yaml
env:
  CATALOG_CONNECTION: "Server=db;Database=catalog;..."
```

Run the same CLI commands:

```bash
modulus migrate update --module Catalog
```

The `dbsh` global tool must be installed in CI:

```bash
dotnet tool install --global dbsh
```

The `modulus doctor` command checks for this and reports a warning when
dbsh is needed but not on `PATH`.

### Programmatic Detection

The framework exposes the engine at the `ModuleModel` level:

```csharp
// True when this module uses dbsh SQL migrations
model.UseDbsh

// The dbsh provider id (e.g. "sqlite", "postgres", "mssql")
model.DbshProvider
```

At runtime, `IModuleMigrationRegistry.IsExternallyManaged(contextType)` is
true for dbsh contexts. The `ExternallyManaged<TContext>` extension method
registers the context in this registry.

## Comparison

| Feature | EF Core | dbsh |
|---------|---------|------|
| Auto-migration | Yes | No (manual SQL) |
| Model diffing | Yes | No (you write SQL) |
| Schema tracking | Migration history table | `dbsh` tracking tables |
| SQL control | Limited (generated) | Full |
| Down-migrations | Generated from model | Manual `.down.sql` files |
| Multi-provider | Yes (auto-diff) | Per-provider SQL |
| External tooling | `dotnet ef` | `dbsh` global tool |
| Framework skip | No | `ExternallyManaged` |
| Startup behavior | `Migrate()`/`EnsureCreated()` | Skipped (dbsh applies) |
