using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Modulus.Identity.Scim;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The application's role class carries an extra column; SCIM groups are the application's roles, not the framework default.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScimExtendedRoleTests : IAsyncLifetime
{
    private static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Identity:Scim:ManagedRoles:0"] = "Buyers" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddModulusAuthorization();
        services.AddSingleton<ICurrentTenant>(new CompanyTenant(s_company));
        services.AddDbContext<AppIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddModulusIdentity<AppIdentityDbContext, ModulusUser, AppRole>(configuration);
        services.AddModulusScim<ModulusUser, AppRole>(configuration);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        (await roles.CreateAsync(new AppRole { Name = "Buyers", TenantId = s_company, Department = "Procurement" })).Succeeded.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Groups_are_the_application_roles_and_keep_their_extra_columns()
    {
        using var scope = _provider.CreateScope();

        var scim = scope.ServiceProvider.GetRequiredService<IScimService>();
        var groups = await scim.ListGroupsAsync(null, 1, 100, "https://localhost/scim/v2", CancellationToken.None);

        groups.TotalResults.Should().Be(1);
        groups.Resources.Single().DisplayName.Should().Be("Buyers");
        scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Roles.AsNoTracking()
            .Single(r => r.Name == "Buyers").Department.Should().Be("Procurement");
    }

    private sealed class CompanyTenant(Guid company) : ICurrentTenant
    {
        public Guid? TenantId => company;

        public string? TenantSlug => null;

        public bool IsAvailable => true;

        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    public sealed class AppRole : ModulusRole
    {
        public string? Department { get; set; }
    }

    public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options, ICurrentTenant currentTenant)
        : ModulusIdentityDbContext<ModulusUser, AppRole>(options, currentTenant)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AppRole>().Property(r => r.Department).HasMaxLength(64);
        }
    }
}
