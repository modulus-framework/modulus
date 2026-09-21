---
sidebar_position: 5
---

# Caching

Modulus provides in-memory caching with tag-based invalidation
(`Modulus.Platform`), and a Redis implementation (`Modulus.Caching.Redis`).

## Setup

```csharp
services.AddModulusCaching();   // in-memory default, no config
```

## Usage

### ICacheService

```csharp
public sealed class GetProductHandler(IProductRepository repository, ICacheService cache)
    : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    public async Task<ProductDto?> HandleAsync(GetProductByIdQuery query, CancellationToken ct)
    {
        var cacheKey = $"product:{query.Id}";

        var cached = await cache.GetAsync<ProductDto>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var product = await repository.GetByIdAsync(query.Id, ct);
        if (product is null)
            return null;

        var dto = new ProductDto { Id = product.Id, Name = product.Name };
        await cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5),
            tags: ["products"], ct);
        return dto;
    }
}
```

### Cache Invalidation

```csharp
// Invalidate by key
await cache.RemoveAsync("product:123", ct);

// Invalidate by tag
await cache.RemoveByTagAsync("products", ct);
await cache.RemoveByTagsAsync(["products", "catalog"], ct);
```

Tags are **tenant-scoped** (`modulus:tag:{tenantId}:{tag}`, or
`modulus:tag:{tag}` on the host) — invalidating a tag only affects the
current tenant's entries.

## Redis Cache

For distributed caching (`Modulus.Caching.Redis` replaces the in-memory
implementation):

```bash
modulus app MyApp --caching redis
```

```csharp
services.AddRedisCacheService(configuration); // reads Caching:Redis:ConnectionString
```

Cross-node invalidation flows through the Redis backplane
(`AddRedisCacheBackplane()`): invalidations publish on
`modulus:cache:invalidate` so every node evicts. Custom L1 caches must evict
locally on notification — never republish.

## See Also

- [Platform Overview](overview) — Other platform services
