using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Governance;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// Recertification campaigns hold a company's access reviews, so they are tenant-owned like grants and delegations. A campaign
/// created in one company must not be readable, listed or decidable from another; the host still sees all of them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecertificationTenantIsolationTests : IDisposable
{
    private static readonly Guid s_alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_manager = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FakeCurrentTenant _tenant = new();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public RecertificationTenantIsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));

        _provider = services.BuildServiceProvider();
        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A_campaign_created_in_one_tenant_is_invisible_from_another()
    {
        var store = _provider.GetRequiredService<EfRecertificationCampaignStore>();
        var items = new RecertificationCampaign("2026-Q3", [ReportFor(s_alice)]).Items.ToList();

        _tenant.EnterTenant(_tenantA);
        var id = await store.CreateAsync("2026-Q3", items, s_manager, CancellationToken.None);

        // Visible to its own company, with its lines (proves the items were stamped with the company too).
        var (_, _, _, total) = (await store.ListActiveAsync(CancellationToken.None)).Should().ContainSingle().Which;
        total.Should().Be(items.Count);
        (await store.GetAsync(id, CancellationToken.None)).Should().NotBeNull();

        _tenant.EnterTenant(_tenantB);
        (await store.ListActiveAsync(CancellationToken.None)).Should().BeEmpty();
        (await store.GetAsync(id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Host_sees_campaigns_from_every_tenant()
    {
        var store = _provider.GetRequiredService<EfRecertificationCampaignStore>();

        _tenant.EnterTenant(_tenantA);
        var idA = await store.CreateAsync("A", new RecertificationCampaign("A", [ReportFor(s_alice)]).Items.ToList(), s_manager, CancellationToken.None);
        _tenant.EnterTenant(_tenantB);
        var idB = await store.CreateAsync("B", new RecertificationCampaign("B", [ReportFor(s_alice)]).Items.ToList(), s_manager, CancellationToken.None);

        _tenant.EnterHost();
        var listed = await store.ListActiveAsync(CancellationToken.None);
        listed.Select(c => c.Id).Should().BeEquivalentTo([idA, idB]);
    }

    private static EffectiveAccessReport ReportFor(Guid user)
        => new(
            user,
            DirectPermissions: new HashSet<string>(["orders:read"], StringComparer.OrdinalIgnoreCase),
            DelegatedPermissions: [new DelegatedPermission("orders:approve", s_manager, Guid.NewGuid())],
            AllPermissions: new HashSet<string>(["orders:read", "orders:approve"], StringComparer.OrdinalIgnoreCase),
            SodViolations: []);

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost { get; private set; }

        public void EnterHost()
        {
            IsHost = true;
            TenantId = null;
        }

        public void EnterTenant(Guid tenantId)
        {
            IsHost = false;
            TenantId = tenantId;
        }

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException("Not exercised by these tests.");
    }
}
