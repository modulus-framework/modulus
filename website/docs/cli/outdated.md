---
sidebar_position: 15
---

# modulus outdated

Shows all packages with newer versions available on NuGet.

## Usage

```bash
modulus outdated [options]
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--framework-only` | Only check `Cobytelabs.Modulus.*` packages | false |
| `-o, --output` | App root directory | Current directory |

## What It Does

1. Scans your project for package references
2. Queries NuGet V3 API for latest stable versions
3. Displays packages with available updates

## Package Categories

| Category | Description |
|----------|-------------|
| **Framework** | `Cobytelabs.Modulus.*` packages |
| **Third-party** | Microsoft, Rebus, Serilog, etc. |

## Example Output

A 5-column table (`Package` / `Current` / `Available` / `Type` / `Update`):

```
Package                          Current    Available  Type         Update
────────────────────────────────────────────────────────────────────────
Microsoft.EntityFrameworkCore     10.0.9     10.0.11    Third-party  Minor
Cobytelabs.Modulus.Core           1.3.0      1.4.0      Framework    Minor
```

## Examples

```bash
# Check all packages
modulus outdated

# Only check framework packages
modulus outdated --framework-only

# Check in a specific directory
modulus outdated --output /path/to/myapp
```

## See Also

- [`update`](update) — Apply available updates
- [`doctor`](doctor) — Check environment health
