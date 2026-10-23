using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>A8-1 durable store: shares are stored per company, survive a restart, and a revocation is kept.</summary>
[Trait("Category", "Unit")]
public sealed class EfRecordShareStoreTests : IDisposable
{
    private static readonly Guid s_companyA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid s_companyB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid s_owner = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid s_grantee = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeCurrentTenant _tenant = new();
    private readonly ServiceProvider _provider;

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => null;
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; }

        public void EnterHost() { IsHost = true; TenantId = null; }
        public void EnterTenant(Guid tenantId) { IsHost = false; TenantId = tenantId; }
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException("Not exercised by these tests.");
    }

    public EfRecordShareStoreTests()
    {
        _connection.Open();
        _provider = Build();
        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static RecordShare Share(Guid? tenant, string id = "inv-1", DateTimeOffset? revoked = null) => new(
        Guid.NewGuid(), tenant, "Invoice", id, s_grantee, s_owner, s_now, s_now.AddHours(1), revoked);

    [Fact]
    public async Task A_share_is_found_by_id_and_by_record_in_its_own_company()
    {
        _tenant.EnterTenant(s_companyA);
        var store = _provider.GetRequiredService<IRecordShareStore>();
        var share = Share(s_companyA);

        await store.AddAsync(share);

        (await store.FindAsync(share.Id)).Should().BeEquivalentTo(share);
        (await store.ForRecordAsync(s_companyA, "Invoice", "inv-1")).Should().ContainSingle().Which.Should().BeEquivalentTo(share);
    }

    [Fact]
    public async Task A_share_is_invisible_from_another_company_and_from_the_host()
    {
        _tenant.EnterTenant(s_companyA);
        await _provider.GetRequiredService<IRecordShareStore>().AddAsync(Share(s_companyA));

        _tenant.EnterTenant(s_companyB);
        var store = _provider.GetRequiredService<IRecordShareStore>();
        (await store.ForRecordAsync(s_companyB, "Invoice", "inv-1")).Should().BeEmpty();
        (await store.ForRecordAsync(s_companyA, "Invoice", "inv-1")).Should().BeEmpty("the ambient company filter also applies, so company B cannot read company A");

        _tenant.EnterHost();
        (await store.ForRecordAsync(null, "Invoice", "inv-1")).Should().BeEmpty("a company's share is not a host share");
    }

    [Fact]
    public async Task A_host_share_is_kept_and_found_for_the_host()
    {
        _tenant.EnterHost();
        var store = _provider.GetRequiredService<IRecordShareStore>();
        var share = Share(tenant: null, id: "host-1");

        await store.AddAsync(share);

        (await store.ForRecordAsync(null, "Invoice", "host-1")).Should().ContainSingle().Which.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task A_revocation_survives_a_restart()
    {
        _tenant.EnterTenant(s_companyA);
        var store = _provider.GetRequiredService<IRecordShareStore>();
        var share = Share(s_companyA);
        await store.AddAsync(share);
        await store.UpdateAsync(share with { RevokedAt = s_now.AddMinutes(5) });

        using var restarted = Build();
        var after = restarted.GetRequiredService<IRecordShareStore>();
        var found = await after.FindAsync(share.Id);

        found.Should().NotBeNull();
        found!.RevokedAt.Should().Be(s_now.AddMinutes(5));
        found.IsActiveAt(s_now.AddMinutes(6)).Should().BeFalse();
    }
}
