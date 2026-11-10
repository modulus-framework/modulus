using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// <c>TopUpCompanyGrantsAsync</c> at start-up: a permission a release adds to the baseline reaches the companies that already exist,
/// without touching what an administrator changed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanyTopUpOnStartupTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;
    private readonly Company _tenant = new();
    private readonly TenantInfo _acme = new(Guid.NewGuid(), "acme");
    private readonly TenantInfo _globex = new(Guid.NewGuid(), "globex");

    public CompanyTopUpOnStartupTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddPermissions("orders", permissions => permissions.Add("orders:read", "Read orders"));
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        services.Configure<CompanyProvisioningOptions>(o => o.Grants = new() { ["Admin"] = ["orders:read"] });
        services.AddSingleton<ITenantStore>(new FakeStore([_acme, _globex]));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task<List<(string Permission, PermissionGrantType Type)>> AdminGrantsAsync(TenantInfo company)
    {
        _tenant.Enter(company);
        try
        {
            return (await _provider.GetRequiredService<EfPermissionGrantStore>().GetGrantsForHolderAsync(GrantHolderType.Role, "Admin"))
                .Select(g => (g.Permission, g.Type)).ToList();
        }
        finally
        {
            _tenant.Enter(null);
        }
    }

    private async Task ProvisionBothAsync()
    {
        using var scope = _provider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ICompanyProvisioner>();
        await provisioner.ProvisionAsync(_acme);
        await provisioner.ProvisionAsync(_globex);
    }

    private void ReleaseAddsAPermission()
    {
        _provider.GetRequiredService<IPermissionRegistry>().Add("orders:export", "Export orders", null, PermissionSensitivity.Normal);
        _provider.GetRequiredService<IOptions<CompanyProvisioningOptions>>().Value.Grants["Admin"] = ["orders:read", "orders:export"];
    }

    [Fact]
    public async Task Every_existing_company_receives_the_new_permission_and_a_deliberate_deny_stays()
    {
        await ProvisionBothAsync();
        _tenant.Enter(_acme);
        await _provider.GetRequiredService<EfPermissionGrantStore>().DenyToRoleAsync("Admin", ["orders:read"]);
        _tenant.Enter(null);
        ReleaseAddsAPermission();

        var added = await _provider.TopUpCompanyGrantsAsync();

        added.Should().Be(2, "orders:export for each of the two companies");
        (await AdminGrantsAsync(_acme)).Should().Contain(("orders:export", PermissionGrantType.Allow)).And.Contain(("orders:read", PermissionGrantType.Deny));
        (await AdminGrantsAsync(_globex)).Should().Contain(("orders:export", PermissionGrantType.Allow));
    }

    [Fact]
    public async Task Running_it_again_adds_nothing()
    {
        await ProvisionBothAsync();
        ReleaseAddsAPermission();
        await _provider.TopUpCompanyGrantsAsync();

        (await _provider.TopUpCompanyGrantsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task It_does_nothing_when_turned_off()
    {
        await ProvisionBothAsync();
        ReleaseAddsAPermission();
        _provider.GetRequiredService<IOptions<CompanyProvisioningOptions>>().Value.TopUpOnStartup = false;

        (await _provider.TopUpCompanyGrantsAsync()).Should().Be(0);
        (await AdminGrantsAsync(_acme)).Should().NotContain(g => g.Permission == "orders:export");
    }

    [Fact]
    public async Task A_host_without_a_tenant_store_does_nothing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new Company());
        services.AddOptions<CompanyProvisioningOptions>();
        services.AddScoped<ICompanyProvisioner, Provisioner>();
        await using var provider = services.BuildServiceProvider();

        (await provider.TopUpCompanyGrantsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task One_failing_company_does_not_stop_the_others()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new Company());
        services.AddOptions<CompanyProvisioningOptions>();
        services.AddSingleton<ITenantStore>(new FakeStore([_acme, _globex]));
        var seen = new List<string>();
        services.AddScoped<ICompanyProvisioner>(_ => new Provisioner(seen, failFor: "acme", failWith: new TimeoutException("store busy")));
        await using var provider = services.BuildServiceProvider();

        (await provider.TopUpCompanyGrantsAsync()).Should().Be(1);

        seen.Should().Equal("acme", "globex");
    }

    [Fact]
    public async Task A_misconfigured_baseline_stops_the_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new Company());
        services.AddOptions<CompanyProvisioningOptions>();
        services.AddSingleton<ITenantStore>(new FakeStore([_acme]));
        services.AddScoped<ICompanyProvisioner>(_ => new Provisioner([], failFor: "acme", failWith: new InvalidOperationException("not a registered permission")));
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.TopUpCompanyGrantsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a registered permission*");
    }

    private sealed class Provisioner(List<string>? seen = null, string? failFor = null, Exception? failWith = null) : ICompanyProvisioner
    {
        public Task<IReadOnlyList<string>> ProvisionAsync(TenantInfo company, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> TopUpAsync(TenantInfo company, CancellationToken ct = default)
        {
            seen?.Add(company.TenantSlug);
            if (company.TenantSlug == failFor && failWith is not null)
                throw failWith;
            return Task.FromResult<IReadOnlyList<string>>(["Admin:orders:export"]);
        }
    }

    private sealed class FakeStore(IReadOnlyList<TenantInfo> companies) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(companies.FirstOrDefault(c => c.TenantId == id));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct) => Task.FromResult(companies.FirstOrDefault(c => c.TenantSlug == slug));

        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct) => Task.FromResult(companies);
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
