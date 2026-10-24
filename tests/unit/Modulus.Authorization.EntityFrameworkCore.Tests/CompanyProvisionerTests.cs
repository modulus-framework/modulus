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
/// The company baseline (D-10): a new company gets the configured grants inside its own scope, the baseline is validated against the
/// registered permissions, and a repeated call adds nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanyProvisionerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly ScopedCompany _tenant = new();
    private readonly TenantInfo _acme = new(Guid.NewGuid(), "acme");
    private readonly TenantInfo _globex = new(Guid.NewGuid(), "globex");

    public CompanyProvisionerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        // AddPermissions only records declarations for the host to replay at startup, so the test registers them directly.
        var registry = _provider.GetRequiredService<IPermissionRegistry>();
        registry.Add("orders:read", "Read orders", null, PermissionSensitivity.Normal);
        registry.Add("orders:admin", "Administer orders", null, PermissionSensitivity.Critical);

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private void Configure(params (string Role, string[] Permissions)[] grants)
    {
        var options = _provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CompanyProvisioningOptions>>().Value;
        options.Grants = grants.ToDictionary(g => g.Role, g => g.Permissions, StringComparer.Ordinal);
    }

    private Task<IReadOnlyCollection<PermissionGrant>> ReadAsync(string role) => Store().GetGrantsAsync(new PrincipalGrantQuery(null, [role]));

    private EfPermissionGrantStore Store() => _provider.GetRequiredService<EfPermissionGrantStore>();

    private ICompanyProvisioner Provisioner() => _provider.GetRequiredService<ICompanyProvisioner>();

    [Fact]
    public async Task An_empty_baseline_grants_nothing()
    {
        await Provisioner().ProvisionAsync(_acme);

        _tenant.Enter(_acme);
        (await ReadAsync("Admin")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_baseline_grants_each_role_inside_the_new_company_only()
    {
        Configure(("Admin", ["orders:read"]));

        await Provisioner().ProvisionAsync(_acme);

        _tenant.Enter(_acme);
        (await ReadAsync("Admin")).Should().ContainSingle(g => g.Permission == "orders:read" && g.Type == PermissionGrantType.Allow);
        _tenant.Enter(_globex);
        (await ReadAsync("Admin")).Should().BeEmpty("another company's grants come from its own baseline");
    }

    [Fact]
    public async Task Provisioning_twice_adds_nothing_the_second_time()
    {
        Configure(("Admin", ["orders:read"]));

        await Provisioner().ProvisionAsync(_acme);
        await Provisioner().ProvisionAsync(_acme);

        _tenant.Enter(_acme);
        (await ReadAsync("Admin")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_baseline_naming_an_unregistered_permission_is_refused_and_writes_nothing()
    {
        Configure(("Admin", ["orders:reed"]));

        var act = () => Provisioner().ProvisionAsync(_acme);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*orders:reed*not a registered permission*");
        _tenant.Enter(_acme);
        (await ReadAsync("Admin")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_critical_permission_is_refused_even_when_it_is_registered()
    {
        Configure(("Admin", ["orders:admin"]));

        var act = () => Provisioner().ProvisionAsync(_acme);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*orders:admin*Critical*");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("orders:*")]
    public async Task A_wildcard_is_refused(string permission)
    {
        Configure(("Admin", [permission]));

        var act = () => Provisioner().ProvisionAsync(_acme);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*wildcard*");
    }

    [Fact]
    public async Task A_reserved_integration_role_cannot_hold_a_company_baseline()
    {
        Configure(("integration:billing", ["orders:read"]));

        var act = () => Provisioner().ProvisionAsync(_acme);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration:billing*");
    }

    /// <summary>A company scope that the test enters and leaves explicitly; <c>Change</c> is the scope the provisioner uses.</summary>
    private sealed class ScopedCompany : ICurrentTenant
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
