using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>The lineage a company carries (the companies whose grants it inherits): itself, then each active company above it.</summary>
[Trait("Category", "Unit")]
public sealed class TenantLineageStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _keepAlive = new("DataSource=:memory:");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _keepAlive.OpenAsync();
        var services = new ServiceCollection();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_keepAlive));
        _services = services.BuildServiceProvider();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    [Fact]
    public async Task A_company_carries_itself_then_each_company_above_it_nearest_first()
    {
        await using var scope = _services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantManager>();
        var holding = (await tenants.CreateAsync("holding", "Holding")).TenantId;
        var branch = (await tenants.CreateAsync("branch", "Branch")).TenantId;
        var subsidiary = (await tenants.CreateAsync("subsidiary", "Subsidiary")).TenantId;
        await tenants.SetParentAsync(branch, holding);
        await tenants.SetParentAsync(subsidiary, branch);
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        (await store.FindByIdAsync(subsidiary, CancellationToken.None))!.Lineage.Should().Equal(subsidiary, branch, holding);
        (await store.FindBySlugAsync("subsidiary", CancellationToken.None))!.Lineage.Should().Equal(subsidiary, branch, holding);
        (await store.FindByIdAsync(holding, CancellationToken.None))!.Lineage.Should().Equal(holding);
    }

    [Fact]
    public async Task An_inactive_company_above_cuts_the_lineage_so_the_grants_above_it_do_not_reach_below()
    {
        await using var scope = _services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantManager>();
        var holding = (await tenants.CreateAsync("holding", "Holding")).TenantId;
        var branch = (await tenants.CreateAsync("branch", "Branch")).TenantId;
        var subsidiary = (await tenants.CreateAsync("subsidiary", "Subsidiary")).TenantId;
        await tenants.SetParentAsync(branch, holding);
        await tenants.SetParentAsync(subsidiary, branch);
        await tenants.SetActiveAsync(branch, false);
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        (await store.FindByIdAsync(subsidiary, CancellationToken.None))!.Lineage.Should().Equal(subsidiary);
    }
}
