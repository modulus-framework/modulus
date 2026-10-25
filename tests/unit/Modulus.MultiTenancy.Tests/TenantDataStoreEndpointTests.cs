using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Modulus.Secrets;
using NSubstitute;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The tenant data-store admin routes, served in-process. The policy is open here so the tests exercise the route's own
/// checks (key format, required fields, host context) and not the permission, which the authorization tests cover. A response
/// must never carry a connection string.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantDataStoreEndpointTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
    private const string Route = "/api/tenancy/data-stores";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The tenant's database is a file, migrated by the provisioning route through the module context routed to "primary".
    // The marker sits in the file name: a SQLite "Password" keyword is refused by this native build, and the response must not echo the path.
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"do-not-return-{Guid.NewGuid():N}.db");
    private string Connection => $"Data Source={_dbPath}";

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly InMemorySecretStore _secrets = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _storeKeepAlive.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddMultiTenancy(t => t.UseHeaderResolver());
        builder.Services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        builder.Services.AddSingleton<ISecretStore>(_secrets);
        builder.Services.AddSingleton<ISecretWriter>(_secrets);
        builder.Services.AddSingleton<ISecretCache>(Substitute.For<ISecretCache>());
        builder.Services.AddSingleton<ITenantDatabaseProviders>(new AcceptingProviders());
        builder.Services.AddSingleton<ITenantDatabaseResolver>(sp => new TenantDatabaseResolver(
            sp.GetRequiredService<IServiceScopeFactory>(), _secrets, sp.GetRequiredService<ISecretCache>(),
            Options.Create(new TenantDataStoreOptions()), TimeProvider.System));
        var route = new TenantDatabaseRoute(typeof(ProvisionedDbContext), "primary", "sqlite");
        builder.Services.AddSingleton(route);
        builder.Services.AddScoped(sp => new ProvisionedDbContext(new DbContextOptionsBuilder<ProvisionedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(new TenantConnectionInterceptor(
                sp.GetRequiredService<ITenantDatabaseResolver>(), sp.GetRequiredService<ICurrentTenant>(), route))
            .Options));
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ProvisionedDbContext>());
        builder.Services.AddAuthorization(o => o.AddPolicy(TenantDataStoreEndpoints.ManagePermission, p => p.RequireAssertion(_ => true)));

        _app = builder.Build();
        _app.MapModulusTenantProvisioning();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();

        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
        await store.Database.EnsureCreatedAsync();
        store.Tenants.Add(new TenantEntity { Id = Tenant, Slug = "acme" });
        await store.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _storeKeepAlive.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Provision_ReturnsTheStore_AndNeverTheConnectionString()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/{Tenant:D}/primary", new ProvisionDataStoreRequest("sqlite", Connection));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("do-not-return");
        var body = JsonSerializer.Deserialize<TenantDataStoreResponse>(text, JsonOptions)!;
        body.Status.Should().Be("Active");
        body.SecretVersion.Should().Be(1);
        (await _secrets.GetCurrentVersionAsync(SecretReference.Parse($"tenant-db/{Tenant:D}/primary"))).Should().Be(1);
    }

    [Fact]
    public async Task Provision_WithAMalformedStoreKey_IsRefused()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/{Tenant:D}/Not_Valid", new ProvisionDataStoreRequest("sqlite", Connection));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provision_WithoutAProvider_IsRefused()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/{Tenant:D}/primary", new ProvisionDataStoreRequest(" ", Connection));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rotate_WithoutAConnectionString_IsRefused()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/{Tenant:D}/primary/rotate", new RotateDataStoreRequest(" "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Read_OfAStoreThatWasNeverProvisioned_IsNotFound()
    {
        var response = await _client.GetAsync($"{Route}/{Tenant:D}/primary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Stands in for the provider probe; the route checks are what these tests cover.</summary>
    private sealed class AcceptingProviders : ITenantDatabaseProviders
    {
        public Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ClearPool(string providerKey, string connectionString)
        {
        }
    }
}
