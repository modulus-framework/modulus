---
sidebar_position: 7
---

# modulus generate-query

Generates a single query and handler.

## Usage

```bash
modulus generate-query <Name> --module <Module> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--module` | Target module name (required) |
| `--response` | Response type (required) |

## What It Generates

### Query

```csharp
public sealed record GetProductStats : IQuery<ProductStatsDto>;
```

### Handler

```csharp
public sealed class GetProductStatsHandler(IProductRepository repository)
    : IQueryHandler<GetProductStats, ProductStatsDto>
{
    public async Task<ProductStatsDto> HandleAsync(
        GetProductStats query, CancellationToken ct)
    {
        // TODO: Implement query logic
        return new ProductStatsDto(0, 0);
    }
}
```

## Example

```bash
modulus generate-query GetProductStats --module Catalog --response ProductStatsDto
modulus generate-query GetOrderHistory --module Orders --response List<OrderDto>
```

## See Also

- [`generate-crud`](generate-crud) — Generate all CRUD operations
- [`generate-command`](generate-command) — Generate a command
