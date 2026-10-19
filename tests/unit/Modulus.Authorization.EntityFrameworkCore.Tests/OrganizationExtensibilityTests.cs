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

/// <summary>
/// The organization entities are extended the way an Identity context is: the application derives its own org unit class with an
/// extra column, derives a context over it, and registers through the generic overload. The org hierarchy and the rest of the
/// authorization stores then work on the application's type.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrganizationExtensibilityTests : IAsyncLifetime
{
    private static readonly Guid Root = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Branch = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores<AppAuthorizationDbContext, AppOrgUnit, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(
            o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppAuthorizationDbContext>>().CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationOrgUnitColumns_PersistAndTheHierarchyWorksOnTheApplicationType()
    {
        using (var scope = _provider.CreateScope())
        {
            var hierarchy = scope.ServiceProvider.GetRequiredService<EfOrgHierarchy<AppOrgUnit, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>>();
            await hierarchy.AddUnitAsync(Root, []);
            await hierarchy.AddUnitAsync(Branch, [Root]);
        }

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppAuthorizationDbContext>>().CreateDbContext();
            var branch = await db.Set<AppOrgUnit>().SingleAsync(u => u.Id == Branch);
            branch.Region = "north";
            await db.SaveChangesAsync();
        }

        using var read = _provider.CreateScope();
        var db2 = read.ServiceProvider.GetRequiredService<IDbContextFactory<AppAuthorizationDbContext>>().CreateDbContext();
        (await db2.Set<AppOrgUnit>().AsNoTracking().SingleAsync(u => u.Id == Branch)).Region.Should().Be("north");

        var hierarchy2 = read.ServiceProvider.GetRequiredService<IOrgHierarchy>();
        hierarchy2.Descendants(Root).Should().Contain(Branch);
    }

    [Fact]
    public void DefaultNames_StillResolve()
    {
        using var scope = _provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>().Should().NotBeNull();
    }

    public sealed class AppOrgUnit : OrgUnitRow
    {
        public string? Region { get; set; }
    }

    public sealed class AppAuthorizationDbContext(DbContextOptions<AppAuthorizationDbContext> options, ICurrentTenant currentTenant)
        : AuthorizationStoreDbContext<AppOrgUnit, OrgUnitParentRow, OrgUnitProfileRow, CompanyProfileRow>(options, currentTenant)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AppOrgUnit>().Property(u => u.Region).HasMaxLength(32);
        }
    }
}
