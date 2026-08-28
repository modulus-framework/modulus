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
| `--verbose` | Show detailed information |

## Example Output

```
Modules:
  Catalog
    Database: SQLite (catalog.db)
    Entities: Product, Category
    Migration: 3 migrations applied

  Orders
    Database: SqlServer (Server=localhost;Database=Orders)
    Entities: Order, OrderItem
    Migration: 2 migrations applied

  Inventory
    Database: PostgreSQL (Host=localhost;Database=Inventory)
    Entities: StockItem, Warehouse
    Migration: 1 migration applied
```

## See Also

- [`info`](info) — Show app overview
- [`doctor`](doctor) — Check environment
