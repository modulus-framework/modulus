using Modulus.Testing;
using System.Net.Http.Json;

namespace TradeFlow.E2ETests;

public class ApiFixture : IAsyncLifetime
{
    private ModulusWebAppFactory<Program>? _factory;
    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("Fixture not initialized");
    public ModulusWebAppFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        _factory = new ModulusWebAppFactory<Program>();
        _client = _factory.CreateClient();
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
    public void SetUser(string userId, string? tenantId = null, string? roles = null, string? permissions = null)
    {
        _client!.DefaultRequestHeaders.Remove(TestAuthDefaults.UserIdHeader);
        _client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId);
        _client.DefaultRequestHeaders.Remove(TestAuthDefaults.UserNameHeader);
        _client.DefaultRequestHeaders.Add(TestAuthDefaults.UserNameHeader, "test-user");

        if (!string.IsNullOrEmpty(tenantId))
        {
            _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
            _client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        }

        if (!string.IsNullOrEmpty(roles))
        {
            _client.DefaultRequestHeaders.Remove(TestAuthDefaults.RolesHeader);
            _client.DefaultRequestHeaders.Add(TestAuthDefaults.RolesHeader, roles);
        }

        if (!string.IsNullOrEmpty(permissions))
        {
            _client.DefaultRequestHeaders.Remove(TestAuthDefaults.PermissionsHeader);
            _client.DefaultRequestHeaders.Add(TestAuthDefaults.PermissionsHeader, permissions);
        }
    }

    /// <summary>
    /// Clear user context
    /// </summary>
    public void ClearUser()
    {
        _client!.DefaultRequestHeaders.Remove(TestAuthDefaults.UserIdHeader);
        _client.DefaultRequestHeaders.Remove(TestAuthDefaults.UserNameHeader);
        _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _client.DefaultRequestHeaders.Remove(TestAuthDefaults.RolesHeader);
    }
}

[CollectionDefinition("API Tests")]
public class ApiTestsCollection : ICollectionFixture<ApiFixture>
{
}
