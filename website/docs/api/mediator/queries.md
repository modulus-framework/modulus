---
sidebar_position: 8
---

# Queries API

## IQuery\<TResponse\>

```csharp
public interface IQuery<TResponse> : IRequest<TResponse> { }
```

## IQueryHandler\<TQuery, TResponse\>

```csharp
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> HandleAsync(TQuery query, CancellationToken ct = default);
}
```

## Example

```csharp
// Query
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;

// Handler
public sealed class GetProductByIdHandler(IProductRepository repository)
    : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    public async Task<ProductDto?> HandleAsync(GetProductByIdQuery query, CancellationToken ct)
    {
        var product = await repository.GetByIdAsync(query.Id, ct);
        return product is null
            ? null
            : new ProductDto { Id = product.Id, Name = product.Name };
    }
}

// Usage
var product = await mediator.QueryAsync(new GetProductByIdQuery(productId));
```
