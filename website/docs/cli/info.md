---
sidebar_position: 10
---

# modulus info

Shows an overview of the application.

## Usage

```bash
modulus info [options]
```

## Example Output

```
Application: MyApp
Root Namespace: MyApp
Host Project: src/API/MyApp.Api/MyApp.Api.csproj

Framework Features:
  ✓ API Versioning
  ✓ Rate Limiting
  ✓ Health Checks
  ✓ CORS
  ✓ Security Headers
  ✓ Idempotency
  ✓ Correlation
  ✓ Secrets Guard
  ✓ Feature Flags
  ✓ Personal Data Protection

Modules (3):
  Catalog
    Entities: Product, Category
    Database: SQLite

  Orders
    Entities: Order, OrderItem
    Database: SqlServer

  Inventory
    Entities: StockItem
    Database: PostgreSQL
```

## See Also

- [`list`](list) — List modules and entities
- [`doctor`](doctor) — Check environment
