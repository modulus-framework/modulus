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
| **.NET SDK** | Installed SDK version (reported, no minimum enforced) |
| **dotnet-ef** | Global tool installed (only when an EF Core module exists) |
| **dbsh** | `dbsh` tool available (only when a dbsh module exists) |
| **App Structure** | Inside a Modulus app, host API project, Program.cs, NuGet.config, .gitignore |
| **Modules** | Per module: Infrastructure project, DbContext, design-time factory, migration engine (`dbsh` or `efcore`) |
| **CLI Version** | Latest CLI tool version on NuGet |
| **Framework Version** | Latest framework package versions |

## Example Output

A 3-column table (`Check` / `Status` / `Detail`) with `✓ ok` / `! warn` / `✗ fail`:

```
Check                              │ Status │ Detail
.NET SDK                           │ ✓ ok   │ 10.0.109
dotnet-ef tool                     │ ✓ ok   │ 10.0.9
Inside a Modulus app               │ ✓ ok   │ MyApp.slnx
Host API project exists            │ ✓ ok   │ src/API/MyApp.Api/…
Catalog: design-time factory       │ ✓ ok   │ src/Modules/…/CatalogDbContextFactory.cs
Catalog: migration engine          │ ✓ ok   │ efcore (Migrations/)
CLI tool version                   │ ✓ ok   │ v1.3.0 (latest)
Framework version                  │ ✓ ok   │ v1.3.0 (latest)
```

## See Also

- [`list`](list) — List modules
- [`info`](info) — Show app overview
