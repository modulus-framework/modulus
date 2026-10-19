using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Events.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Modulus.Secrets;
using NSubstitute;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// Provisioning and rotation against the real catalog (SQLite) and an in-memory secret store, through the resolver that the
/// application uses, so a test sees exactly what a request would see.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantDataStoreProvisionerTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    private const string Store = "primary";
    private const string Provider = "sqlite";

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly InMemorySecretStore _secrets = new();
    private readonly ISecretCache _cache = Substitute.For<ISecretCache>();
    private readonly FakeProviders _databases = new();
    private readonly RecordingBus _bus = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _storeKeepAlive.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        services.AddSingleton<ISecretStore>(_secrets);
        services.AddSingleton<ISecretWriter>(_secrets);
        services.AddSingleton(_cache);
        services.AddSingleton<ITenantDatabaseProviders>(_databases);
        services.AddSingleton<IModuleBus>(_bus);
        services.AddSingleton<ITenantDatabaseResolver>(sp => new TenantDatabaseResolver(
            sp.GetRequiredService<IServiceScopeFactory>(), _secrets, _cache, Options.Create(new TenantDataStoreOptions()), TimeProvider.System));
        _provider = services.BuildServiceProvider();
        await EnsureStoreSchemaAsync();
        await SeedTenantAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        _storeKeepAlive.Dispose();
    }

    [Fact]
    public async Task Provision_WritesTheSecret_ActivatesTheRow_AndTheResolverSeesIt()
    {
        var record = await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=one.db"));

        record.Status.Should().Be(TenantDataStoreStatus.Active);
        record.SecretVersion.Should().Be(1);
        record.SecretPath.Should().Be($"tenant-db/{Tenant:D}/{Store}");
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=one.db");
    }

    [Fact]
    public async Task Provision_OfAnActiveStore_IsIdempotent()
    {
        await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=one.db"));

        var again = await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=other.db"));

        again.SecretVersion.Should().Be(1, "a second provision must not write a new version");
        (await _secrets.GetCurrentVersionAsync(Ref())).Should().Be(1);
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=one.db");
    }

    [Fact]
    public async Task Provision_CompletesAStoreLeftProvisioning_ByAnInterruptedRun()
    {
        // The row exists and the secret was written, but the run stopped before the row was activated.
        await _secrets.WriteAsync(Ref(), new Dictionary<string, string> { ["connectionString"] = "Data Source=old.db" }, null);
        await InsertRowAsync(TenantDataStoreStatus.Provisioning, secretVersion: 0);

        var record = await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=new.db"));

        record.Status.Should().Be(TenantDataStoreStatus.Active);
        record.SecretVersion.Should().Be(2);
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=new.db");
    }

    [Fact]
    public async Task Provision_OfADisabledStore_IsRefused()
    {
        await InsertRowAsync(TenantDataStoreStatus.Disabled, secretVersion: 0);

        Func<Task> act = () => WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=x.db"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Rotate_SwapsTheConnectionString_BumpsTheVersion_AndTheResolverSeesIt()
    {
        await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=one.db"));
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=one.db");

        var rotated = await WithProvisionerAsync(p => p.RotateAsync(Tenant, Store, "Data Source=two.db"));

        rotated.Status.Should().Be(TenantDataStoreStatus.Active);
        rotated.SecretVersion.Should().Be(2);
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=two.db");
    }

    [Fact]
    public async Task Rotate_WithoutAProvisionedStore_IsNotFound()
    {
        Func<Task> act = () => WithProvisionerAsync(p => p.RotateAsync(Tenant, Store, "Data Source=x.db"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Rotate_OfAStoreThatIsNotActive_IsRefused()
    {
        await InsertRowAsync(TenantDataStoreStatus.Provisioning, secretVersion: 0);

        Func<Task> act = () => WithProvisionerAsync(p => p.RotateAsync(Tenant, Store, "Data Source=x.db"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    private static SecretReference Ref() => SecretReference.Parse($"tenant-db/{Tenant:D}/{Store}");

    [Fact]
    public async Task Provision_RefusedByTheProbe_WritesNothing_AndLeavesTheStoreProvisioning()
    {
        _databases.FailProbe = true;

        Func<Task> act = () => WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=x.db"));

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
        (await _secrets.GetCurrentVersionAsync(Ref())).Should().BeNull("an unreachable database must not reach the secret store");
        (await ReadRowAsync()).Status.Should().Be(TenantDataStoreStatus.Provisioning);
        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_PublishesTheChange_OnceTheStoreIsActive()
    {
        await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=one.db"));

        var changed = _bus.Published.OfType<TenantDataStoreChanged>().Should().ContainSingle().Subject;
        changed.TenantId.Should().Be(Tenant);
        changed.StoreKey.Should().Be(Store);
        changed.ProviderKey.Should().Be(Provider);
        changed.SecretVersion.Should().Be(1);
    }

    [Fact]
    public async Task Rotate_ClearsThePoolOfTheReplacedString_WhenTheChangeIsHandled()
    {
        await WithProvisionerAsync(p => p.ProvisionAsync(Tenant, Store, Provider, "Data Source=one.db"));
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=one.db");

        await WithProvisionerAsync(p => p.RotateAsync(Tenant, Store, "Data Source=two.db"));
        await DeliverLastChangeAsync();

        _databases.Cleared.Should().ContainSingle().Which.Should().Be((Provider, "Data Source=one.db"));
        (await ResolveAsync()).ConnectionString.Should().Be("Data Source=two.db");
    }

    private async Task DeliverLastChangeAsync()
    {
        var changed = _bus.Published.OfType<TenantDataStoreChanged>().Last();
        var handler = new TenantDataStoreChangedHandler(_provider.GetRequiredService<ITenantDatabaseResolver>(), _databases);
        await handler.HandleAsync(changed, CancellationToken.None);
    }

    private async Task<TenantDataStore> ReadRowAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantDataStoreCatalog>().FindAsync(Tenant, Store)
            ?? throw new InvalidOperationException("The test expected a catalog row.");
    }

    private async Task SeedTenantAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
        db.Tenants.Add(new TenantEntity { Id = Tenant, Slug = "acme" });
        await db.SaveChangesAsync();
    }

    private async Task<TenantDataStore> WithProvisionerAsync(Func<ITenantDataStoreProvisioner, Task<TenantDataStore>> call)
    {
        using var scope = _provider.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<ITenantDataStoreProvisioner>());
    }

    private async Task<TenantDatabaseTarget> ResolveAsync()
        => await _provider.GetRequiredService<ITenantDatabaseResolver>().ResolveAsync(Tenant, Store, Provider);

    private async Task EnsureStoreSchemaAsync()
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    private async Task InsertRowAsync(TenantDataStoreStatus status, int secretVersion)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
        db.TenantDataStores.Add(new TenantDataStoreEntity
        {
            TenantId = Tenant,
            StoreKey = Store,
            Isolation = TenantDataIsolation.DatabasePerTenant,
            ProviderKey = Provider,
            SecretPath = $"tenant-db/{Tenant:D}/{Store}",
            SecretVersion = secretVersion,
            Status = status,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Stands in for the provider probe and records the pools that were cleared.</summary>
    private sealed class FakeProviders : ITenantDatabaseProviders
    {
        public bool FailProbe { get; set; }

        public List<(string ProviderKey, string ConnectionString)> Cleared { get; } = [];

        public Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken = default)
            => FailProbe ? throw new InvalidOperationException("The database refused the connection.") : Task.CompletedTask;

        public void ClearPool(string providerKey, string connectionString) => Cleared.Add((providerKey, connectionString));
    }

    /// <summary>Keeps what the provisioner publishes, in order.</summary>
    private sealed class RecordingBus : IModuleBus
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
