using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The five-project module layout (Domain, Application, IntegrationEvents, Infrastructure, Presentation) that
/// matches the garments-erp modules: integration events are their own project, which Application references.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public class ModuleLayoutTests
{
    private static ModuleModel CatalogModel(bool legacy = false) => new()
    {
        RootNamespace = "MyApp",
        ModuleNamespace = "MyApp.Modules.Catalog",
        ModuleName = "Catalog",
        DbProvider = "SQLite",
        MigrationEngine = "efcore",
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
        LegacyIntegrationEvents = legacy,
    };

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modulus-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GenerateModule_writes_an_integration_events_project_that_only_references_the_events_package()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            new NewAppCommand().GenerateModule(dir, CatalogModel());

            var csproj = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.IntegrationEvents", "MyApp.Modules.Catalog.IntegrationEvents.csproj"));
            csproj.Should().Contain("Synvro.Modulus.Events").And.NotContain("ProjectReference");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateModule_puts_events_in_the_events_project_under_the_events_namespace()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            new NewAppCommand().GenerateModule(dir, CatalogModel());

            var eventsDir = Path.Combine(dir, "MyApp.Modules.Catalog.IntegrationEvents", "IntegrationEvents");
            File.Exists(Path.Combine(eventsDir, "CatalogArea.cs")).Should().BeTrue();
            File.Exists(Path.Combine(eventsDir, "ProductCreatedIntegrationEvent.cs")).Should().BeTrue();
            File.ReadAllText(Path.Combine(eventsDir, "ProductCreatedIntegrationEvent.cs"))
                .Should().Contain("namespace MyApp.Modules.Catalog.IntegrationEvents;");

            Directory.Exists(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "IntegrationEvents")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateModule_application_references_the_events_project_and_handlers_use_its_namespace()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            new NewAppCommand().GenerateModule(dir, CatalogModel());

            var appCsproj = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "MyApp.Modules.Catalog.Application.csproj"));
            appCsproj.Should().Contain("../MyApp.Modules.Catalog.IntegrationEvents/MyApp.Modules.Catalog.IntegrationEvents.csproj");

            var handler = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "CreateProductHandler.cs"));
            handler.Should().Contain("using MyApp.Modules.Catalog.IntegrationEvents;");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Generated_create_and_update_handlers_check_the_resulting_record_scope()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            new NewAppCommand().GenerateModule(dir, CatalogModel());

            var appCsproj = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "MyApp.Modules.Catalog.Application.csproj"));
            appCsproj.Should().Contain("Synvro.Modulus.Platform");

            var create = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "CreateProductHandler.cs"));
            create.Should().Contain("IResourceScopeGuard? scopeGuard = null")
                .And.Contain("scopeGuard.EnsureResultingScopeAsync(entity, \"create\", null, ct)")
                .And.Contain("fieldAuthorizer.EnsureWriteAllowedAsync(typeof(Product), fields, ct)");

            var update = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "UpdateProductHandler.cs"));
            update.Should().Contain("scopeGuard.EnsureResultingScopeAsync(entity, \"edit\", null, ct)")
                .And.Contain("fieldAuthorizer.EnsureWriteAllowedAsync(typeof(Product), fields, ct)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Generated_write_handlers_check_the_endpoint_permission_when_the_host_enforces_one()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            var model = CatalogModel();
            model.RequiredPermission = "catalog:products:manage";
            new NewAppCommand().GenerateModule(dir, model);

            var create = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "CreateProductHandler.cs"));
            create.Should().Contain("EnsureResultingScopeAsync(entity, \"create\", \"catalog:products:manage\", ct)");

            var update = File.ReadAllText(Path.Combine(dir, "MyApp.Modules.Catalog.Application", "UpdateProductHandler.cs"));
            update.Should().Contain("EnsureResultingScopeAsync(entity, \"edit\", \"catalog:products:manage\", ct)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Blank_module_still_generates_the_events_project_it_references()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            new NewAppCommand().GenerateModule(dir, new ModuleModel
            {
                RootNamespace = "MyApp",
                ModuleNamespace = "MyApp.Modules.Ledger",
                ModuleName = "Ledger",
                DbProvider = "SQLite",
                MigrationEngine = "efcore",
            });

            File.Exists(Path.Combine(dir, "MyApp.Modules.Ledger.IntegrationEvents", "MyApp.Modules.Ledger.IntegrationEvents.csproj")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Module_projects_include_the_integration_events_project_between_domain_and_application()
    {
        var paths = NewAppCommand.ModuleProjectPaths("MyApp", "Catalog").ToList();

        paths.Should().Equal(
            "src/Modules/MyApp.Modules.Catalog/MyApp.Modules.Catalog.Domain/MyApp.Modules.Catalog.Domain.csproj",
            "src/Modules/MyApp.Modules.Catalog/MyApp.Modules.Catalog.IntegrationEvents/MyApp.Modules.Catalog.IntegrationEvents.csproj",
            "src/Modules/MyApp.Modules.Catalog/MyApp.Modules.Catalog.Application/MyApp.Modules.Catalog.Application.csproj",
            "src/Modules/MyApp.Modules.Catalog/MyApp.Modules.Catalog.Infrastructure/MyApp.Modules.Catalog.Infrastructure.csproj",
            "src/Modules/MyApp.Modules.Catalog/MyApp.Modules.Catalog.Presentation/MyApp.Modules.Catalog.Presentation.csproj");
    }

    [Fact]
    public void Legacy_module_keeps_the_application_events_namespace()
    {
        CatalogModel().IntegrationEventsNamespace.Should().Be("MyApp.Modules.Catalog.IntegrationEvents");
        CatalogModel(legacy: true).IntegrationEventsNamespace.Should().Be("MyApp.Modules.Catalog.Application.IntegrationEvents");
    }
}
