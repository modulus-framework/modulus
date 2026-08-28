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

### When to Use

- You prefer writing SQL migrations manually
- You need provider-specific optimizations
- You want to avoid EF Core's model diffing

### Setup

```bash
modulus app MyApp --migration-engine dbsh
```

### Structure

```
Catalog.Infrastructure/
├── Database/
│   ├── dbsh.toml
│   └── Migrations/
│       └── .gitkeep
```

### Configuration

```toml
# dbsh.toml
[module]
name = "catalog"

[database]
type = "sqlite"
connection = "Data Source=catalog.db"
```

### CLI Commands

```bash
# Create a new migration
dbsh create AddProductsTable

# Apply migrations
dbsh migrate

# Validate migrations
dbsh validate
```

### Framework Integration

dbsh modules register their context as externally managed:

```csharp
services.AddModuleDatabase<CatalogDbContext>(config)
    .ExternallyManaged<CatalogDbContext>();
```

The startup skips auto-migration for externally managed contexts.

## Comparison

| Feature | EF Core | dbsh |
|---------|---------|------|
| Auto-migration | Yes | No |
| Model diffing | Yes | Manual |
| SQL control | Limited | Full |
| Multi-provider | Yes | Per-provider SQL |
| MongoDB support | No | No |
