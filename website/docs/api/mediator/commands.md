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
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

// Handler
public sealed class CreateProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        var product = new Product { Name = command.Name };
        await repo.AddAsync(product, ct);
        await unitOfWork.CommitAsync(ct);
        return product.Id;
    }
}

// Usage
var id = await mediator.SendAsync(new CreateProductCommand("Widget"));
```
