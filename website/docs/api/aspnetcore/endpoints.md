---
sidebar_position: 10
---

# Endpoints API

## Minimal API Style

Implement `IEndpoint` or `IMinimalEndpoint`:

```csharp
public sealed class GetProductsEndpoint : IEndpoint
{
    public void Configure(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", HandleAsync)
            .WithName("GetProducts")
            .Produces<List<ProductDto>>();
    }

    private static async Task<IResult> HandleAsync(
        IMediator mediator)
    {
        var products = await mediator.QueryAsync(new GetAllProducts());
        return Results.Ok(products);
    }
}
```

Register and map:

```csharp
// Program.cs
builder.Services.AddEndpoints(typeof(Program).Assembly);
app.MapEndpoints();
```

## REPR Pattern

Inherit `Endpoint<TRequest, TResponse>`:

```csharp
public sealed class GetProductEndpoint
    : Endpoint<GetProductRequest, ProductDto>
{
    public override void Configure()
    {
        Get("/api/products/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(
        GetProductRequest req,
        CancellationToken ct)
    {
        var product = await Mediator.QueryAsync(
            new GetProductById(req.Id), ct);
        await SendAsync(product, ct);
    }
}
```

Map:

```csharp
app.MapModulusEndpoints();
```

## See Also

- [Module System](/docs/architecture/module-system) — Module registration
