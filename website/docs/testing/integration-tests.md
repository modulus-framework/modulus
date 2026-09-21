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
        userId: Guid.NewGuid(),
        userName: "Test User",
        roles: ["admin"],
        permissions: ["catalog.products.create"]);

    var response = await client.PostAsJsonAsync("/api/catalog/products", new
    {
        name = "Widget"
    });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

`CreateAuthenticatedClient(Guid? userId, string? userName, string? email,
IEnumerable<string>? roles/permissions, Guid? tenantId)` — omitted userId
becomes a random Guid, userName defaults to `test-user`; `tenantId` is sent
as `X-Tenant-Id` (requires the app's tenant store to resolve it).

The `TestAuthHandler` processes headers:

| Header | Value |
|--------|-------|
| `X-Test-UserId` | User ID |
| `X-Test-UserName` | User name |
| `X-Test-Email` | User email |
| `X-Test-Roles` | Comma-separated roles |
| `X-Test-Permissions` | Comma-separated permissions |

## Database Isolation

Isolation is per **factory instance**: tests sharing one factory (e.g. via a
shared `IClassFixture`) share databases; a new factory gets a fresh set:

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
            userName: "Test User");
    }

    [Fact]
    public async Task PostGet_RoundTrip()
    {
        // Create
        var createResponse = await _client.PostAsJsonAsync("/api/catalog/products", new
        {
            name = "Widget"
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
