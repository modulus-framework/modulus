---
sidebar_position: 8
---

# modulus migrate

Database migration commands.

## Usage

```bash
modulus migrate add <Name> [options]
modulus migrate update [options]
```

## migrate add

Scaffolds a new EF Core migration.

### Options

| Option | Description |
|--------|-------------|
| `--module` | Specific module (default: all) |

### Examples

```bash
# Add migration to all modules
modulus migrate add InitialCreate

# Add migration to specific module
modulus migrate add AddProductsTable --module Catalog
```

### What It Does

1. Runs `dotnet ef migrations add` in each module's Infrastructure project
2. Generates migration files in `Infrastructure/Migrations/`
3. For dbsh modules, runs `dbsh create` instead

## migrate update

Applies pending migrations.

### Options

| Option | Description |
|--------|-------------|
| `--module` | Specific module (default: all) |

### Examples

```bash
# Update all modules
modulus migrate update

# Update specific module
modulus migrate update --module Catalog
```

### What It Does

1. Runs `dotnet ef database update` for each module
2. For dbsh modules, runs `dbsh migrate` instead
3. For modules without migrations, runs `EnsureCreated`

## Automatic Migrations at Startup

```csharp
// Program.cs
await app.Services.MigrateModulusDatabasesAsync();
```

| Mode | Behavior |
|------|----------|
| `MigrateOrCreate` (default) | `Migrate()` when migrations exist, else `EnsureCreated()` |
| `Migrate` | Always applies migrations; throws if none |
| `EnsureCreated` | Snapshot only; no migration history |

## See Also

- [Migrations](../data/migrations) — Migration concepts
