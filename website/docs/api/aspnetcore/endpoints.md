---
sidebar_position: 10
---

# Endpoints API

## Minimal API Style

Implement `IEndpoint` or `IMinimalEndpoint`:

```csharp
public sealed class GetProductsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalog/products", HandleAsync)
            .WithName("GetProducts")
            .Produces<List<ProductDto>>();
    }

    private static async Task<IResult> HandleAsync(IMediator mediator, CancellationToken ct)
    {
        var products = await mediator.QueryAsync(new GetProductsQuery(), ct);
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

Inherit `Endpoint<TRequest, TResponse>` (request binding + validation are
automatic; use `SendOkAsync`/`SendCreatedAsync`, or `SendAsync` with an
explicit status code):

```csharp
public sealed class GetProductEndpoint
    : Endpoint<GetProductRequest, ProductDto>
{
    public override void Configure()
    {
        Get("/api/catalog/products/{Id}");
        RequireAuthorization();
    }

    public override async Task HandleAsync(
        GetProductRequest req,
        CancellationToken ct)
    {
        var product = await Mediator.QueryAsync(
            new GetProductByIdQuery(req.Id), ct);
        await SendOkAsync(product, ct);
    }
}
```

Map:

```csharp
app.MapModulusEndpoints();
```

## See Also

- [Module System](/docs/architecture/module-system) — Module registration
