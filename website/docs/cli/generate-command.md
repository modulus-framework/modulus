---
sidebar_position: 6
---

# modulus generate-command

Generates a single command and handler.

## Usage

```bash
modulus generate-command <Name> [-m|--module <Module>] [options]
```

## Options

| Option | Description |
|--------|-------------|
| `-m, --module` | Target module name. Auto-detected when the app has a single module |

## What It Generates

### Command

```csharp
public sealed record ArchiveProductCommand : ICommand<Unit>;
```

### Handler

```csharp
public sealed class ArchiveProductHandler
    : ICommandHandler<ArchiveProductCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        ArchiveProductCommand command,
        CancellationToken ct)
    {
        throw new NotImplementedException(); // implement ArchiveProduct here
    }
}
```

The handler throws `NotImplementedException` until you implement it. Add the
module's `IUnitOfWork` / repositories as needed.

## Example

```bash
modulus generate-command ArchiveProduct --module Catalog
modulus generate-command CancelOrder --module Orders
```

## See Also

- [`generate-crud`](generate-crud) — Generate all CRUD operations
- [`generate-query`](generate-query) — Generate a query
