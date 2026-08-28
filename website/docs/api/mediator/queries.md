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
public sealed record GetProductById(Guid Id) : IQuery<ProductDto>;

// Handler
public sealed class GetProductByIdHandler(IProductRepository repository)
    : IQueryHandler<GetProductById, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductById query, CancellationToken ct)
    {
        var product = await repository.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(Product), query.Id);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}

// Usage
var product = await mediator.QueryAsync(new GetProductById(productId));
```
