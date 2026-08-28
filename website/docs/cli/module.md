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
| `--migration-engine` | efcore (default) or dbsh |

## Generated Structure

```
MyApp.Modules.Products/
├── .Domain/
│   ├── Product.cs
│   └── IProductRepository.cs
├── .Application/
│   ├── IUnitOfWork.cs
│   ├── Dtos/
│   ├── Commands/
│   ├── Queries/
│   └── IntegrationEvents/
├── .Infrastructure/
│   ├── ProductsDbContext.cs
│   ├── ProductsDbContextFactory.cs
│   ├── ProductRepository.cs
│   └── ProductsModule.cs
└── .Presentation/
    └── ProductController.cs
```

## Example

```bash
modulus module Products
```

## See Also

- [`add-module`](add-module) — Add module to existing app
