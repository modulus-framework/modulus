using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>A context over the framework's own organization rows, registered through the generic overload (an application's own module does this).</summary>
public sealed class FrameworkRowsAuthorizationContext(DbContextOptions options, ICurrentTenant currentTenant)
    : AuthorizationStoreDbContext<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(options, currentTenant);

/// <summary>
/// The management API and the framework's endpoints take the non-generic organization stores. Through the generic overload with the
/// framework's rows they must resolve to the same instances as the generic types, or the endpoints fail to bind (as a body) and
/// two caches can disagree.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FrameworkRowsThroughGenericOverloadTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores<FrameworkRowsAuthorizationContext, OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(
            o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void The_non_generic_organization_stores_resolve()
    {
        using var scope = _provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<EfOrgHierarchy>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<EfOrganizationProfileStore>().Should().NotBeNull();
    }

    [Fact]
    public void The_non_generic_and_generic_organization_stores_are_one_instance_each()
    {
        using var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;

        ((object)sp.GetRequiredService<EfOrgHierarchy<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>>())
            .Should().BeSameAs(sp.GetRequiredService<EfOrgHierarchy>());
        ((object)sp.GetRequiredService<EfOrganizationProfileStore<OrgUnitRow, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>>())
            .Should().BeSameAs(sp.GetRequiredService<EfOrganizationProfileStore>());
    }

    [Fact]
    public void The_organization_hierarchy_interface_is_the_same_instance()
    {
        using var scope = _provider.CreateScope();

        ((object)scope.ServiceProvider.GetRequiredService<IOrgHierarchy>())
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<EfOrgHierarchy>());
    }
}
