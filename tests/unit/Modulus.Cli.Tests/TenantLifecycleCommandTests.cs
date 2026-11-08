using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary><c>modulus add-tenant-lifecycle</c> on generated multi-tenant apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class TenantLifecycleCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-lifecycle-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string Infra = "src/Modules/Shop.Modules.TenantLifecycle/Shop.Modules.TenantLifecycle.Infrastructure";

    private void GenerateApp(bool multiTenancy)
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = "openiddict",
            Kind = AppKind.Api,
            MultiTenancy = multiTenancy,
        });
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private static int Run(string dir) => new AddTenantLifecycleCommand().ExecuteCore(new AddTenantLifecycleCommand.Settings(), dir);

    [Fact]
    public void Refuses_an_app_without_multi_tenancy()
    {
        GenerateApp(multiTenancy: false);

        var act = () => Run(AppDir);

        act.Should().Throw<InvalidOperationException>().WithMessage("*multi-tenancy*");
        Directory.Exists(Path.Combine(AppDir, "src/Modules/Shop.Modules.TenantLifecycle")).Should().BeFalse();
    }

    [Fact]
    public void Adds_the_store_module_host_wiring_settings_and_tests()
    {
        GenerateApp(multiTenancy: true);

        Run(AppDir).Should().Be(0);

        Read($"{Infra}/Shop.Modules.TenantLifecycle.Infrastructure.csproj").Should().Contain("Synvro.Modulus.Tenancy.Lifecycle.EntityFrameworkCore");
        Read($"{Infra}/AppTenantLifecycleDbContext.cs").Should().Contain("namespace Shop.Modules.TenantLifecycle.Infrastructure;")
            .And.Contain(": TenantLifecycleDbContext(options)");
        Read($"{Infra}/AppTenantLifecycleDbContextFactory.cs").Should().Contain("TENANT_LIFECYCLE_CONNECTION");
        Read($"{Infra}/TenantLifecycleModule.cs").Should().Contain("AddEfTenantLifecycleStore<AppTenantLifecycleDbContext>()");
        Read("Shop.slnx").Should().Contain("Shop.Modules.TenantLifecycle.Infrastructure.csproj");
        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Shop.Modules.TenantLifecycle.Infrastructure.csproj");

        var program = ApiHostFiles.ReadAll(Path.Combine(AppDir, "src/API/Shop.Api/Program.cs"));
        program.Should().Contain("modules.AddModule<TenantLifecycleModule>();");
        program.Should().Contain("AddTenantLifecycle(");
        program.Should().Contain("AddFileStorage(builder.Configuration)", "a generated multi-tenant host registers no file storage");
        program.Should().Contain("MapModulusTenantLifecycle()");

        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"))
            .GetProperty("ConnectionStrings").GetProperty("TenantLifecycle").GetString().Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(AppDir, "tests/Shop.Tests/TenantLifecycleTests.cs")).Should().BeTrue();
    }

    [Fact]
    public void Administrators_of_each_new_company_start_with_the_export_permission()
    {
        GenerateApp(multiTenancy: true);

        Run(AppDir).Should().Be(0);

        var grants = WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"))
            .GetProperty("CompanyProvisioning").GetProperty("Grants");
        grants.GetProperty("Admin").EnumerateArray().Select(e => e.GetString()).Should().Contain("tenant-data:export");
    }

    private const string ProvisioningWith = "{\n  \"CompanyProvisioning\": {\n    \"Grants\": {\n@@    }\n  }\n}\n";

    private static string Provisioning(string grants) => ProvisioningWith.Replace("@@", grants, StringComparison.Ordinal);

    [Fact]
    public void EnsureCompanyGrant_appends_to_the_role_and_is_idempotent()
    {
        var json = Provisioning("      \"Admin\": [ \"catalog:products:manage\" ]\n");

        var once = TenantLifecycleWiring.EnsureCompanyGrant(json, "Admin", "tenant-data:export");
        var twice = TenantLifecycleWiring.EnsureCompanyGrant(once, "Admin", "tenant-data:export");

        WebhooksWiringTests.Parse(once).GetProperty("CompanyProvisioning").GetProperty("Grants").GetProperty("Admin")
            .EnumerateArray().Select(e => e.GetString()).Should().Equal("catalog:products:manage", "tenant-data:export");
        twice.Should().Be(once);
    }

    [Fact]
    public void EnsureCompanyGrant_adds_the_role_to_an_empty_or_other_roles_section()
    {
        var empty = TenantLifecycleWiring.EnsureCompanyGrant(Provisioning(string.Empty), "Admin", "tenant-data:export");
        var other = TenantLifecycleWiring.EnsureCompanyGrant(
            Provisioning("      \"Buyer\": [ \"orders:read\" ]\n"), "Admin", "tenant-data:export");

        WebhooksWiringTests.Parse(empty).GetProperty("CompanyProvisioning").GetProperty("Grants").GetProperty("Admin")
            .EnumerateArray().Select(e => e.GetString()).Should().Equal("tenant-data:export");
        var grants = WebhooksWiringTests.Parse(other).GetProperty("CompanyProvisioning").GetProperty("Grants");
        grants.GetProperty("Buyer").EnumerateArray().Select(e => e.GetString()).Should().Equal("orders:read");
        grants.GetProperty("Admin").EnumerateArray().Select(e => e.GetString()).Should().Equal("tenant-data:export");
    }

    [Fact]
    public void EnsureCompanyGrant_leaves_a_file_without_company_grants_alone()
    {
        const string json = "{\n  \"Logging\": {}\n}\n";

        TenantLifecycleWiring.EnsureCompanyGrant(json, "Admin", "tenant-data:export").Should().Be(json);
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        GenerateApp(multiTenancy: true);
        Run(AppDir);
        var files = Directory.EnumerateFiles(AppDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);

        Run(AppDir).Should().Be(0);

        foreach (var (path, content) in files)
            File.ReadAllText(path).Should().Be(content, path);
    }

    public void Dispose()
    {
        Ux.Quiet = false;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
