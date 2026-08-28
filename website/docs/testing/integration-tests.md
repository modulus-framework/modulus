---
sidebar_position: 2
---

# Integration Tests

Modulus provides `ModulusWebAppFactory` for HTTP-based integration testing.

## Setup

```csharp
public sealed class CatalogTests : IClassFixture<ModulusWebAppFactory<Program>>
{
    private readonly HttpClient _client;

    public CatalogTests(ModulusWebAppFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProducts_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

## ModulusWebAppFactory

The factory:

1. Boots the real host with all middleware and mediator pipeline
2. Swaps every module `DbContext` to its own in-memory SQLite database
3. Opens keep-alive connections per database
4. Runs `EnsureCreated` per context

```csharp
public sealed class TestWebAppFactory : ModulusWebAppFactory<Program>
{
    // Custom test configuration
}
```

## Authenticated Requests

```csharp
[Fact]
public async Task CreateProduct_AsAuthenticatedUser_ReturnsCreated()
{
    var client = factory.CreateAuthenticatedClient(
        userId: "test-user-id",
        userName: "Test User");

    var response = await client.PostAsJsonAsync("/api/products", new
    {
        name = "Widget",
        price = 9.99
    });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

The `TestAuthHandler` processes headers:

| Header | Value |
|--------|-------|
| `X-Test-User-Id` | User ID |
| `X-Test-User-Name` | User name |
| `X-Test-User-Email` | User email |

## Database Isolation

Each test class gets a fresh set of databases:

- Unique `Cache=Shared` name per factory instance
- Keep-alive connections prevent SQLite from being disposed
- `EnsureCreated` runs after host build

## Full Example

```csharp
[Trait("Category", "Integration")]
public sealed class ProductApiTests : IClassFixture<ModulusWebAppFactory<Program>>
{
    private readonly HttpClient _client;

    public ProductApiTests(ModulusWebAppFactory<Program> factory)
    {
        _client = factory.CreateAuthenticatedClient(
            userId: "test-user",
            userName: "Test User");
    }

    [Fact]
    public async Task PostGet_RoundTrip()
    {
        // Create
        var createResponse = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "Widget",
            price = 9.99m
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var product = await createResponse.Content.ReadFromJsonAsync<ProductDto>();

        // Get
        var getResponse = await _client.GetAsync($"/api/products/{product.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
        fetched.Name.Should().Be("Widget");
    }
}
```

## See Also

- [Testing Overview](overview) — Unit test patterns
