---
sidebar_position: 7
---

# modulus generate-query

Generates a single query and handler.

## Usage

```bash
modulus generate-query <Name> [-m|--module <Module>] [options]
```

## Options

| Option | Description |
|--------|-------------|
| `-m, --module` | Target module name. Auto-detected when the app has a single module |

## What It Generates

### Query

```csharp
public sealed record GetProductStatsQuery : IQuery<object>;
```

### Handler

```csharp
public sealed class GetProductStatsHandler : IQueryHandler<GetProductStatsQuery, object>
{
    public async Task<object> HandleAsync(
        GetProductStatsQuery query,
        CancellationToken ct)
    {
        // TODO: Implement GetProductStats logic here
        return await Task.FromResult(new { });
    }
}
```

The handler returns a placeholder object — replace `object` with a real DTO
and inject the module's repository as needed.

## Example

```bash
modulus generate-query GetProductStats --module Catalog
modulus generate-query GetOrderHistory --module Orders
```

## See Also

- [`generate-crud`](generate-crud) — Generate all CRUD operations
- [`generate-command`](generate-command) — Generate a command
