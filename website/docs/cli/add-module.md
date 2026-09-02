---
sidebar_position: 4
---

# modulus add-module

Adds a layered module to an existing Modulus application.

## Usage

```bash
modulus add-module <name> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--migration-engine` | efcore (default) or dbsh |

## What It Does

1. Creates 4 projects under `src/Modules/{App}.Modules.{Module}/`
2. Registers the module in `Program.cs` (`modules.AddModule<{Module}Module>()`)
3. Adds `ProjectReference`s to the host `.csproj`
4. Adds projects to the `.slnx` solution

## Example

```bash
# From the app root
modulus add-module Products
modulus add-module Orders
modulus add-module Inventory
```

## See Also

- [`module`](module) — Create standalone module
- [`generate-crud`](generate-crud) — Generate CRUD operations
