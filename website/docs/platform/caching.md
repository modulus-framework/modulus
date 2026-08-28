---
sidebar_position: 5
---

# Caching

Modulus provides in-memory caching with tag-based invalidation.

## Setup

```csharp
services.AddModulusCaching(config);
```

## Configuration

```json
{
  "Caching": {
    "DefaultExpirationMinutes": 5,
    "MaxCacheSize": 10000
  }
}
```

## Usage

### ICacheService

```csharp
public sealed class GetProductHandler(IProductRepository repository, ICacheService cache)
    : IQueryHandler<GetProductById, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductById query, CancellationToken ct)
    {
        var cacheKey = $"product:{query.Id}";

        return await cache.GetOrCreateAsync(cacheKey, async () =>
        {
            var product = await repository.GetByIdAsync(query.Id, ct);
            return new ProductDto(product.Id, product.Name, product.Price);
        }, tags: new[] { "products" });
    }
}
```

### Cache Invalidation

```csharp
// Invalidate by key
await cache.RemoveAsync("product:123");

// Invalidate by tag
await cache.InvalidateTagAsync("products");
```

## Redis Cache

For distributed caching:

```bash
modulus app MyApp --cache redis
```

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

```csharp
services.AddModulusCaching(config)
    .UseRedis(config);
```

## See Also

- [Platform Overview](overview) — Other platform services
