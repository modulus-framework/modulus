---
sidebar_position: 6
---

# modulus generate-command

Generates a single command and handler.

## Usage

```bash
modulus generate-command <Name> --module <Module> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--module` | Target module name (required) |
| `--response` | Response type (default: none) |

## What It Generates

### Command

```csharp
public sealed record ArchiveProduct(Guid Id) : ICommand;
```

### Handler

```csharp
public sealed class ArchiveProductHandler(ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveProduct>
{
    public async Task<Unit> HandleAsync(ArchiveProduct command, CancellationToken ct)
    {
        // TODO: Implement handler logic
        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

## With Response

```bash
modulus generate-command ArchiveProduct --module Catalog --response ProductDto
```

Generates:

```csharp
public sealed record ArchiveProduct(Guid Id) : ICommand<ProductDto>;

public sealed class ArchiveProductHandler(ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(ArchiveProduct command, CancellationToken ct)
    {
        // TODO: Implement handler logic
        await unitOfWork.SaveChangesAsync(ct);
        return new ProductDto(command.Id, "", 0);
    }
}
```

## Example

```bash
modulus generate-command ArchiveProduct --module Catalog
modulus generate-command CancelOrder --module Orders --response OrderDto
```

## See Also

- [`generate-crud`](generate-crud) — Generate all CRUD operations
- [`generate-query`](generate-query) — Generate a query
