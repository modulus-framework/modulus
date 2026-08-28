---
sidebar_position: 7
---

# Commands API

## ICommand\<TResponse\>

```csharp
public interface ICommand<TResponse> : IRequest<TResponse> { }
```

## ICommand (no response)

```csharp
public interface ICommand : IRequest<Unit> { }
```

## ICommandHandler\<TCommand, TResponse\>

```csharp
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<TResponse> HandleAsync(TCommand command, CancellationToken ct = default);
}
```

## Sending Commands

```csharp
public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
```

## Example

```csharp
// Command
public sealed record CreateProduct(string Name, decimal Price) : ICommand<ProductDto>;

// Handler
public sealed class CreateProductHandler(ICatalogUnitOfWork unitOfWork)
    : ICommandHandler<CreateProduct, ProductDto>
{
    public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        unitOfWork.Products.Add(product);
        await unitOfWork.SaveChangesAsync(ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}

// Usage
var result = await mediator.SendAsync(new CreateProduct("Widget", 9.99m));
```
