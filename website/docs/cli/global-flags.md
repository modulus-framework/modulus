---
sidebar_position: 12
---

# Global Flags

All CLI commands support these flags.

## Flags

| Flag | Short | Description |
|------|-------|-------------|
| `--dry-run` | | Preview changes without writing files |
| `--force` | | Overwrite existing files without prompting |
| `--verbose` | `-v` | Show detailed output |
| `--quiet` | `-q` | Suppress non-essential output |

## Examples

```bash
# Preview what would be generated
modulus generate-crud Product --module Catalog --dry-run

# Force overwrite without prompts
modulus add-module Catalog --force

# Verbose output for debugging
modulus app MyApp --verbose

# Quiet mode (errors only)
modulus migrate update --quiet
```
