---
sidebar_position: 11
---

# modulus doctor

Checks the development environment for issues.

## Usage

```bash
modulus doctor [options]
```

## What It Checks

| Check | Description |
|-------|-------------|
| **.NET SDK** | Version 10.0.109+ installed |
| **dotnet-ef** | Global tool installed |
| **App Structure** | Host project, Program.cs, NuGet.config |
| **Modules** | Infrastructure projects, DbContext, design-time factories |
| **Git** | .gitignore configured |

## Example Output

```
Environment Checks:
  ✓ .NET SDK 10.0.109
  ✓ dotnet-ef 10.0.9

App Structure:
  ✓ Host project: src/API/MyApp.Api/MyApp.Api.csproj
  ✓ Program.cs
  ✓ NuGet.config
  ✓ .gitignore

Modules:
  ✓ Catalog.Infrastructure
    ✓ CatalogDbContext
    ✓ CatalogDbContextFactory
  ✓ Orders.Infrastructure
    ✓ OrdersDbContext
    ✓ OrdersDbContextFactory
  ✗ Inventory.Infrastructure
    ✗ Missing design-time factory
```

## See Also

- [`list`](list) — List modules
- [`info`](info) — Show app overview
