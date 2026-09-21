---
sidebar_position: 9
---

# modulus list

Lists all modules and their entities.

## Usage

```bash
modulus list [options]
```

## Options

| Option | Description |
|--------|-------------|
| `-o, --output` | App root directory (default: current directory) |

Run from inside a Modulus application. Global `-v` only raises log detail.

## Example Output

A 4-column table (`Module` / `Provider` / `Entities` / `Migrations`),
where `Migrations` is `yes`/`no` (whether the module has authored migrations):

```
╭──────────────────────────────────────────────────╮
│ Modules in MyApp.slnx                            │
│ Module    │ Provider │ Entities        │ Migrations │
│ Catalog   │ SQLite   │ Product         │ yes        │
│ Orders    │ SQLite   │ Order, OrderItem│ no         │
╰──────────────────────────────────────────────────╯
```

## See Also

- [`info`](info) — Show app overview
- [`doctor`](doctor) — Check environment
