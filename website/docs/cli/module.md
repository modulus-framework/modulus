---
sidebar_position: 3
---

# modulus module

Creates a blank 4-layer business module.

## Usage

```bash
modulus module <name> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--app` | Root namespace of the application (auto-detected when omitted) |
| `-o, --output` | Output directory |
| `-d, --database` | Database provider: `SQLite` (default), `SqlServer`, `PostgreSQL`, `MySQL` |
| `--migration-engine` | `efcore` or `dbsh`. Omit to inherit from the app's existing modules |

## Generated Structure

```
MyApp.Modules.Products/
├── .Domain/
│   ├── Product.cs              # AggregateRoot<Guid> sample entity
│   └── IProductRepository.cs
├── .Application/
│   ├── IUnitOfWork.cs          # CommitAsync
│   ├── Dtos/
│   └── IntegrationEvents/      # e.g. ProductCreatedIntegrationEvent
├── .Infrastructure/
│   ├── ProductsDbContext.cs
│   ├── ProductsDbContextFactory.cs
│   ├── ProductRepository.cs
│   └── ProductsModule.cs
└── .Presentation/
    └── ProductsEndpoint.cs     # REPR endpoints (Endpoint<> classes, RequireAuthorization)
```

## Example

```bash
modulus module Products
```

## See Also

- [`add-module`](add-module) — Add module to existing app
