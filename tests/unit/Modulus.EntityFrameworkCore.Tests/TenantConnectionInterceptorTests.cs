namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.EntityFrameworkCore.Isolation;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TenantConnectionInterceptorTests
{
    private const string HostConnection = "Data Source=host;Mode=Memory;Cache=Shared";
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task Host_open_after_a_tenant_open_restores_the_host_connection_string()
    {
        var tenant = new FakeTenant();
        var resolver = new FakeResolver(new Dictionary<Guid, string>
        {
            [TenantA] = "Data Source=tenant-a;Mode=Memory;Cache=Shared",
        });
        var interceptor = new TenantConnectionInterceptor(resolver, tenant, Route);
        using var connection = new SqliteConnection(HostConnection);

        tenant.Set(TenantA);
        await interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None);
        connection.ConnectionString.Should().Be(Normalize("Data Source=tenant-a;Mode=Memory;Cache=Shared"));

        tenant.SetHost();
        await interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None);

        connection.ConnectionString.Should().Be(Normalize(HostConnection));
    }

    [Fact]
    public async Task Tenant_open_after_a_host_open_points_at_the_tenant_database()
    {
        var tenant = new FakeTenant();
        var resolver = new FakeResolver(new Dictionary<Guid, string>
        {
            [TenantA] = "Data Source=tenant-a;Mode=Memory;Cache=Shared",
            [TenantB] = "Data Source=tenant-b;Mode=Memory;Cache=Shared",
        });
        var interceptor = new TenantConnectionInterceptor(resolver, tenant, Route);
        using var connection = new SqliteConnection(HostConnection);

        tenant.SetHost();
        await interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None);
        connection.ConnectionString.Should().Be(Normalize(HostConnection));

        tenant.Set(TenantB);
        await interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None);
        connection.ConnectionString.Should().Be(Normalize("Data Source=tenant-b;Mode=Memory;Cache=Shared"));
    }

    [Fact]
    public async Task Retargeting_an_open_connection_is_refused()
    {
        var tenant = new FakeTenant();
        var resolver = new FakeResolver(new Dictionary<Guid, string>
        {
            [TenantA] = "Data Source=tenant-a;Mode=Memory;Cache=Shared",
            [TenantB] = "Data Source=tenant-b;Mode=Memory;Cache=Shared",
        });
        var interceptor = new TenantConnectionInterceptor(resolver, tenant, Route);
        using var connection = new SqliteConnection(HostConnection);

        tenant.Set(TenantA);
        await interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None);
        await connection.OpenAsync();

        tenant.Set(TenantB);
        var act = () => interceptor.ConnectionOpeningAsync(connection, default!, default, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        connection.ConnectionString.Should().Be(Normalize("Data Source=tenant-a;Mode=Memory;Cache=Shared"));
    }

    // Microsoft.Data.Sqlite rewrites a connection string into its own key order; compare in that form.
    private static string Normalize(string connectionString) => new SqliteConnection(connectionString).ConnectionString;

    private static TenantDatabaseRoute Route => new(typeof(FakeContext), "primary", "sqlite");

    private sealed class FakeContext;

    private sealed class FakeTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => null;
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; }

        public void Set(Guid id)
        {
            TenantId = id;
            IsHost = false;
        }

        public void SetHost()
        {
            TenantId = null;
            IsHost = true;
        }

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    private sealed class FakeResolver(IReadOnlyDictionary<Guid, string> targets) : ITenantDatabaseResolver
    {
        public Task<TenantDatabaseTarget> ResolveAsync(Guid tenantId, string storeKey, string providerKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantDatabaseTarget(tenantId, storeKey, providerKey, targets[tenantId], 1));

        public bool TryGetCached(Guid tenantId, string storeKey, string providerKey, out TenantDatabaseTarget? target)
        {
            target = null;
            return false;
        }

        public void Evict(Guid tenantId, string storeKey) { }

        public bool TryTakeSuperseded(Guid tenantId, string storeKey, out TenantDatabaseTarget? target)
        {
            target = null;
            return false;
        }
    }
}
