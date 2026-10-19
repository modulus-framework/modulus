using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Dimensions;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// Dimension hierarchy: a value placed under another (a warehouse under a region) is covered by an assignment to the region. The
/// placement is refused when it would close a loop, and a company's placements never reach another company.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DimensionHierarchyTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly MutableTenant _tenant = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>()
            .CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task An_assignment_to_a_region_covers_the_warehouses_placed_under_it()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var region = await registry.RegisterAsync("region", "EU", "Europe");
        var warehouse = await registry.RegisterAsync("warehouse", "WH-01", "Main");
        await registry.SetParentAsync("warehouse", "WH-01", "region", "EU");

        var covered = Hierarchy().Expand(new HashSet<Guid> { region.Id });

        covered.Should().Contain(new[] { region.Id, warehouse.Id });
    }

    [Fact]
    public async Task Placements_are_followed_through_more_than_one_level()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var region = await registry.RegisterAsync("region", "EU", "Europe");
        var site = await registry.RegisterAsync("site", "SITE-1", "Site");
        var bin = await registry.RegisterAsync("bin", "BIN-9", "Bin");
        await registry.SetParentAsync("site", "SITE-1", "region", "EU");
        await registry.SetParentAsync("bin", "BIN-9", "site", "SITE-1");

        Hierarchy().Expand(new HashSet<Guid> { region.Id }).Should().Contain(new[] { site.Id, bin.Id });
    }

    [Fact]
    public async Task A_value_that_is_not_placed_beneath_the_assignment_is_not_covered()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var region = await registry.RegisterAsync("region", "EU", "Europe");
        var other = await registry.RegisterAsync("warehouse", "WH-02", "Elsewhere");

        Hierarchy().Expand(new HashSet<Guid> { region.Id }).Should().NotContain(other.Id);
    }

    [Fact]
    public async Task Placing_a_value_under_one_of_its_own_children_is_refused()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        await registry.RegisterAsync("region", "EU", "Europe");
        await registry.RegisterAsync("warehouse", "WH-01", "Main");
        await registry.SetParentAsync("warehouse", "WH-01", "region", "EU");

        var act = () => registry.SetParentAsync("region", "EU", "warehouse", "WH-01");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_value_cannot_be_placed_under_itself()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        await registry.RegisterAsync("region", "EU", "Europe");

        var act = () => registry.SetParentAsync("region", "EU", "region", "EU");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_parent_registered_in_another_company_is_not_found()
    {
        _tenant.TenantId = CompanyB;
        await Registry().RegisterAsync("region", "EU", "Europe");

        _tenant.TenantId = CompanyA;
        var registry = Registry();
        await registry.RegisterAsync("warehouse", "WH-01", "Main");

        var act = () => registry.SetParentAsync("warehouse", "WH-01", "region", "EU");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Another_companys_placements_do_not_widen_this_companys_expansion()
    {
        _tenant.TenantId = CompanyB;
        var otherRegistry = Registry();
        var otherRegion = await otherRegistry.RegisterAsync("region", "EU", "Europe");
        var otherWarehouse = await otherRegistry.RegisterAsync("warehouse", "WH-01", "Main");
        await otherRegistry.SetParentAsync("warehouse", "WH-01", "region", "EU");

        _tenant.TenantId = CompanyA;
        var region = await Registry().RegisterAsync("region", "EU", "Europe");

        var covered = Hierarchy().Expand(new HashSet<Guid> { region.Id });

        covered.Should().NotContain(otherWarehouse.Id);
        otherRegion.Id.Should().NotBe(region.Id);
    }

    [Fact]
    public async Task Moving_a_value_back_to_the_top_removes_it_from_the_assignment()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var region = await registry.RegisterAsync("region", "EU", "Europe");
        var warehouse = await registry.RegisterAsync("warehouse", "WH-01", "Main");
        await registry.SetParentAsync("warehouse", "WH-01", "region", "EU");

        var moved = await registry.SetParentAsync("warehouse", "WH-01", null, null);

        moved.ParentId.Should().BeNull();
        Hierarchy().Expand(new HashSet<Guid> { region.Id }).Should().NotContain(warehouse.Id);
    }

    [Fact]
    public void The_database_hierarchy_replaces_the_framework_default()
    {
        Hierarchy().Should().BeOfType<EfDimensionHierarchy>();
    }

    private IDimensionValueRegistry Registry()
        => _provider.CreateScope().ServiceProvider.GetRequiredService<IDimensionValueRegistry>();

    private IDimensionHierarchy Hierarchy()
        => _provider.CreateScope().ServiceProvider.GetRequiredService<IDimensionHierarchy>();

    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
