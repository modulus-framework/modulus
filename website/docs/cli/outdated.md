---
sidebar_position: 12
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
| **Third-party** | Microsoft, Serilog, MassTransit, etc. |

## Example Output

```
───────────────────────── Modulus outdated ─────────────────────────

> Scanning 27 packages for updates...

Package                          Current    Latest     Type
────────────────────────────────────────────────────────────────────
Microsoft.EntityFrameworkCore     9.0.0      10.0.11    Major
Microsoft.Extensions.Caching      9.0.0      10.0.10    Major
Serilog                           3.1.1      4.0.0      Minor
xunit                             2.8.1      2.9.0      Patch

4 update(s) available.
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
