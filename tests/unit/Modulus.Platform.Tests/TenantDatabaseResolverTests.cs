using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Secrets;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class TenantDatabaseResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("3f2c0000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("3f2c0000-0000-0000-0000-000000000002");
    private const string Store = "primary";
    private const string Provider = "sqlite";

    private readonly FakeCatalog _catalog = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly ISecretCache _cache = Substitute.For<ISecretCache>();
    private readonly FakeClock _clock = new() { Now = DateTimeOffset.Parse("2026-10-18T10:00:00Z") };

    private TenantDatabaseResolver Build(ISecretStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantDataStoreCatalog>(_ => _catalog);
        var root = services.BuildServiceProvider();
        return new TenantDatabaseResolver(
            root.GetRequiredService<IServiceScopeFactory>(),
            store ?? _secrets,
            _cache,
            Options.Create(new TenantDataStoreOptions()),
            _clock);
    }

    private static SecretReference SecretOf(Guid tenant) => SecretReference.Parse($"tenant-db/{tenant:D}/{Store}");

    private static TenantDataStore Row(
        Guid tenant,
        TenantDataIsolation isolation = TenantDataIsolation.DatabasePerTenant,
        TenantDataStoreStatus status = TenantDataStoreStatus.Active,
        string? provider = null,
        string? secretPath = null,
        int secretVersion = 1)
        => new(tenant, Store, isolation, provider ?? Provider, secretPath ?? $"tenant-db/{tenant:D}/{Store}", secretVersion, status);

    private async Task SeedSecretAsync(Guid tenant, string connectionString = "Data Source=tenant-one.db")
    {
        await _secrets.WriteAsync(SecretOf(tenant), new Dictionary<string, string> { ["connectionString"] = connectionString }, null);
    }

    [Fact]
    public async Task ActiveTenant_ResolvesItsConnectionString()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        await SeedSecretAsync(Tenant);

        var target = await Build().ResolveAsync(Tenant, Store, Provider);

        target.TenantId.Should().Be(Tenant);
        target.ConnectionString.Should().Be("Data Source=tenant-one.db");
        target.SecretVersion.Should().Be(1);
    }

    [Fact]
    public async Task RotatingTenant_IsStillServed()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, status: TenantDataStoreStatus.Rotating);
        await SeedSecretAsync(Tenant);

        var target = await Build().ResolveAsync(Tenant, Store, Provider);

        target.ConnectionString.Should().NotBeNull();
    }

    [Fact]
    public async Task NoCatalogRow_IsRefused()
    {
        await SeedSecretAsync(Tenant);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
    }

    [Theory]
    [InlineData(TenantDataStoreStatus.Provisioning)]
    [InlineData(TenantDataStoreStatus.Disabled)]
    public async Task StoreThatDoesNotServe_IsRefused(TenantDataStoreStatus status)
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, status: status);
        await SeedSecretAsync(Tenant);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
    }

    [Fact]
    public async Task ProviderMismatch_IsRefused()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, provider: "postgresql");
        await SeedSecretAsync(Tenant);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>().WithMessage("*another database provider*");
    }

    [Fact]
    public async Task SecretPointingAtAnotherTenant_IsRefused()
    {
        // A catalog edit points this tenant at the other tenant's secret. The canonical path check refuses it.
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, secretPath: $"tenant-db/{Other:D}/{Store}");
        await SeedSecretAsync(Other, "Data Source=other.db");

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        var error = await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
        error.Which.Message.Should().Contain("unexpected secret");
    }

    [Fact]
    public async Task SharedStore_ResolvesToTheHostConnection()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, isolation: TenantDataIsolation.Shared, secretPath: string.Empty);

        var target = await Build().ResolveAsync(Tenant, Store, Provider);

        target.ConnectionString.Should().BeNull();
    }

    [Fact]
    public async Task MissingSecret_IsRefused()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>();
    }

    [Fact]
    public async Task SecretWithoutAConnectionString_IsRefused()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        await _secrets.WriteAsync(SecretOf(Tenant), new Dictionary<string, string> { ["password"] = "x" }, null);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>().WithMessage("*no connection string*");
    }

    [Fact]
    public async Task SecretOlderThanTheCatalog_IsEvictedAndRefused()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant, secretVersion: 2);
        await SeedSecretAsync(Tenant);

        Func<Task> act = () => Build().ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<TenantDatabaseUnavailableException>().WithMessage("*older*");
        _cache.Received(1).Evict(Arg.Is<SecretReference>(r => r.Path == SecretOf(Tenant).Path));
    }

    [Fact]
    public async Task SecretStoreOutage_PropagatesAsUnavailable()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        var store = Substitute.For<ISecretStore>();
        store.GetAsync(Arg.Any<SecretReference>(), Arg.Any<CancellationToken>())
            .Returns<Task<SecretValue>>(_ => throw new SecretStoreUnavailableException("down"));

        Func<Task> act = () => Build(store).ResolveAsync(Tenant, Store, Provider);

        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }

    [Fact]
    public async Task ResolvedTarget_IsCachedWithinTheTtl()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        await SeedSecretAsync(Tenant);
        var resolver = Build();

        await resolver.ResolveAsync(Tenant, Store, Provider);
        await resolver.ResolveAsync(Tenant, Store, Provider);
        _catalog.Calls.Should().Be(1);

        _clock.Now = _clock.Now.AddSeconds(31);
        await resolver.ResolveAsync(Tenant, Store, Provider);
        _catalog.Calls.Should().Be(2);
    }

    [Fact]
    public async Task TryGetCached_OnlyAnswersAfterAResolve_AndAfterEvict_NotAgain()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        await SeedSecretAsync(Tenant);
        var resolver = Build();

        resolver.TryGetCached(Tenant, Store, Provider, out _).Should().BeFalse();

        await resolver.ResolveAsync(Tenant, Store, Provider);
        resolver.TryGetCached(Tenant, Store, Provider, out var target).Should().BeTrue();
        target!.ConnectionString.Should().Be("Data Source=tenant-one.db");
        resolver.TryGetCached(Tenant, Store, "postgresql", out _).Should().BeFalse("a different provider is a different target");

        resolver.Evict(Tenant, Store);
        resolver.TryGetCached(Tenant, Store, Provider, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Primary")]
    [InlineData("pri/mary")]
    [InlineData("primary-store-name-that-is-far-too-long-for-a-key")]
    public async Task InvalidStoreKey_IsRejectedBeforeAnyLookup(string storeKey)
    {
        Func<Task> act = () => Build().ResolveAsync(Tenant, storeKey, Provider);

        await act.Should().ThrowAsync<ArgumentException>();
        _catalog.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Target_DoesNotPrintTheConnectionString()
    {
        _catalog.Rows[(Tenant, Store)] = Row(Tenant);
        await SeedSecretAsync(Tenant, "Host=db.internal;Password=hunter2");

        var target = await Build().ResolveAsync(Tenant, Store, Provider);

        target.ToString().Should().NotContain("hunter2").And.NotContain("Host=");
    }

    private sealed class FakeCatalog : ITenantDataStoreCatalog
    {
        public Dictionary<(Guid TenantId, string StoreKey), TenantDataStore> Rows { get; } = [];

        public int Calls { get; private set; }

        public Task<TenantDataStore?> FindAsync(Guid tenantId, string storeKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Rows.TryGetValue((tenantId, storeKey), out var row) ? row : null);
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
