using Modulus.Testing;
using System.Net.Http.Json;

namespace ModulusSampleErp.IntegrationTests;

public class ApiFixture : IAsyncLifetime
{
    private ModulusWebAppFactory<Program>? _factory;
    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        _factory = new ModulusWebAppFactory<Program>();
        _client = _factory.CreateClient();

        // Seed the database with test data
        await _factory.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
    }

    /// <summary>
    /// Set current user and tenant for subsequent requests
    /// </summary>
    public void SetUser(string userId, string? tenantId = null, string? roles = null)
    {
        _client!.DefaultRequestHeaders.Remove("X-User-Id");
        _client.DefaultRequestHeaders.Add("X-User-Id", userId);

        if (!string.IsNullOrEmpty(tenantId))
        {
            _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
            _client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        }

        if (!string.IsNullOrEmpty(roles))
        {
            _client.DefaultRequestHeaders.Remove("X-Roles");
            _client.DefaultRequestHeaders.Add("X-Roles", roles);
        }
    }

    /// <summary>
    /// Clear user context
    /// </summary>
    public void ClearUser()
    {
        _client!.DefaultRequestHeaders.Remove("X-User-Id");
        _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _client.DefaultRequestHeaders.Remove("X-Roles");
    }
}

[Collection("API Tests")]
public class ApiTestsCollection : ICollectionFixture<ApiFixture>
{
}
