using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The provisioner re-applies the modules' permission declarations before it validates the baseline. After the host has started the
/// registry is frozen, and provisioning a company then must still work: the declarations are unchanged, so nothing is added.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanyProvisionerAfterStartTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;
    private readonly Company _tenant = new();

    public CompanyProvisionerAfterStartTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddPermissions("orders", permissions => permissions.Add("orders:read", "Read orders"));
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        services.Configure<CompanyProvisioningOptions>(o => o.Grants = new() { ["Admin"] = ["orders:read"] });
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A_company_can_be_provisioned_after_the_registry_is_frozen()
    {
        // What the host does when it starts: the declarations reach the registry, then it is frozen. The provisioner still holds the
        // declarations and replays them while it validates the baseline.
        var registry = _provider.GetRequiredService<IPermissionRegistry>();
        registry.Add("orders:read", "Read orders", null, PermissionSensitivity.Normal);
        ((Modulus.Authorization.PermissionRegistry)registry).Freeze();
        var company = new TenantInfo(Guid.NewGuid(), "acme");

        var granted = await _provider.GetRequiredService<ICompanyProvisioner>().ProvisionAsync(company);

        granted.Should().Equal("Admin");
        _tenant.Enter(company);
        (await _provider.GetRequiredService<EfPermissionGrantStore>().GetGrantsAsync(new PrincipalGrantQuery(null, ["Admin"])))
            .Select(g => g.Permission).Should().Equal("orders:read");
    }

    [Fact]
    public async Task Top_up_adds_a_new_baseline_permission_to_an_existing_company_and_keeps_deliberate_changes()
    {
        var company = new TenantInfo(Guid.NewGuid(), "old-co");
        var provisioner = _provider.GetRequiredService<ICompanyProvisioner>();
        await provisioner.ProvisionAsync(company); // the company as it was created: Admin holds orders:read

        _tenant.Enter(company);
        var store = _provider.GetRequiredService<EfPermissionGrantStore>();
        await store.DenyToRoleAsync("Admin", ["orders:read"]); // a deliberate change
        _tenant.Enter(null);

        // A release adds a permission to the baseline.
        var registry = _provider.GetRequiredService<IPermissionRegistry>();
        registry.Add("orders:export", "Export orders", null, PermissionSensitivity.Normal);
        _provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CompanyProvisioningOptions>>().Value.Grants["Admin"] = ["orders:read", "orders:export"];

        (await provisioner.ProvisionAsync(company)).Should().BeEmpty("a role that holds grants is left alone");
        var added = await provisioner.TopUpAsync(company);

        added.Should().Equal("Admin:orders:export");
        _tenant.Enter(company);
        var grants = await store.GetGrantsForHolderAsync(GrantHolderType.Role, "Admin");
        grants.Should().Contain(g => g.Permission == "orders:export" && g.Type == PermissionGrantType.Allow);
        grants.Should().Contain(g => g.Permission == "orders:read" && g.Type == PermissionGrantType.Deny, "the deny stays");
        _tenant.Enter(null);
        (await provisioner.TopUpAsync(company)).Should().BeEmpty("it is safe to repeat");
    }

    private sealed class Company : ICurrentTenant
    {
        private TenantInfo? _current;

        public Guid? TenantId => _current?.TenantId;
        public string? TenantSlug => _current?.TenantSlug;
        public bool IsAvailable => true;
        public bool IsHost => _current is null;

        public void Enter(TenantInfo? tenant) => _current = tenant;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = _current;
            _current = tenant;
            return new Restore(() => _current = previous);
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
