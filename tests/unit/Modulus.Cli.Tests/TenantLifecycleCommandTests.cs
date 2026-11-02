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
