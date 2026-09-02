using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// Covers the dual migration-engine support: EF Core (default) and dbsh
/// (SQL-first, external tool). Engine choice is per app + per module; dbsh
/// modules are detected by the Database/Config/migration.json marker and
/// register their DbContext as externally managed so startup skips it.
/// </summary>
[Trait("Category", "Unit")]
public class MigrationEngineTests
{
    private readonly TemplateEngine _engine = new();

    private static ModuleModel CatalogModel(string engine = "efcore", string provider = "SQLite") => new()
    {
        RootNamespace = "MyApp",
        ModuleNamespace = "MyApp.Modules.Catalog",
        ModuleName = "Catalog",
        DbProvider = provider,
        MigrationEngine = engine,
        EntityName = "Product",
        EntityNameLower = "product",
        RouteName = "products",
    };

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modulus-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Model defaults + derived properties ───────────────────────────

    [Fact]
    public void AppModel_defaults_to_efcore()
    {
        var model = new AppModel();
        model.MigrationEngine.Should().Be("efcore");
        model.UseDbsh.Should().BeFalse();
    }

    [Fact]
    public void AppModel_dbsh_engine_sets_flag()
    {
        var model = new AppModel { MigrationEngine = "dbsh" };
        model.UseDbsh.Should().BeTrue();
    }

    [Fact]
    public void ModuleModel_defaults_to_efcore()
    {
        var model = new ModuleModel();
        model.MigrationEngine.Should().Be("efcore");
        model.UseDbsh.Should().BeFalse();
    }

    [Theory]
    [InlineData("SQLite", "sqlite")]
    [InlineData("SqlServer", "sqlserver")]
    [InlineData("PostgreSQL", "postgresql")]
    [InlineData("MySQL", "mysql")]
    public void ModuleModel_maps_dbsh_provider_ids(string efProvider, string dbshProvider)
    {
        var model = CatalogModel(provider: efProvider);
        model.DbshProvider.Should().Be(dbshProvider);
    }

    [Fact]
    public void ModuleModel_derives_connection_env_var_from_module_name()
    {
        CatalogModel().DbshConnectionEnvVar.Should().Be("CATALOG_CONNECTION");
        new ModuleModel { ModuleName = "OrderHistory" }.DbshConnectionEnvVar
            .Should().Be("ORDERHISTORY_CONNECTION");
    }

    // ── Engine choice validation ──────────────────────────────────────

    [Theory]
    [InlineData("efcore", "efcore")]
    [InlineData("EFCORE", "efcore")]
    [InlineData("dbsh", "dbsh")]
    [InlineData("Dbsh", "dbsh")]
    public void ValidateChoice_normalises_engine_values(string provided, string canonical)
    {
        NewAppCommand.ValidateChoice(
            provided, NewAppCommand.KnownMigrationEngines, "migration engine")
            .Should().Be(canonical);
    }

    [Theory]
    [InlineData("flyway")]
    [InlineData("ef")]
    [InlineData("")]
    public void ValidateChoice_rejects_unknown_engines(string provided)
    {
        var act = () => NewAppCommand.ValidateChoice(
            provided, NewAppCommand.KnownMigrationEngines, "migration engine");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown migration engine*");
    }

    // ── Templates ─────────────────────────────────────────────────────

    [Fact]
    public void Dbsh_migration_json_template_renders_provider_envvar_and_tracking()
    {
        var output = _engine.Render("module/Infrastructure/dbsh.migration.json", CatalogModel(engine: "dbsh"));

        output.Should().NotContain("{{");
        output.Should().Contain("\"provider\": \"sqlite\"");
        output.Should().Contain("\"connectionString\": \"${CATALOG_CONNECTION}\"");
        output.Should().Contain("\"path\": \"./Database/Migrations\"");
    }

    [Fact]
    public void Dbsh_migration_json_template_maps_postgres_provider()
    {
        var output = _engine.Render(
            "module/Infrastructure/dbsh.migration.json",
            CatalogModel(engine: "dbsh", provider: "PostgreSQL"));

        output.Should().Contain("\"provider\": \"postgresql\"");
    }

    [Fact]
    public void Dbsh_local_json_template_has_no_connection_override()
    {
        var output = _engine.Render(
            "module/Infrastructure/dbsh.local.json", CatalogModel(engine: "dbsh"));

        output.Should().NotContain("{{");
        output.Should().Contain("\"name\": \"local\"");
        // No connectionString here: dbsh environment files override
        // migration.json, which would defeat the ${MODULE}_CONNECTION
        // placeholder resolution. The connection must come from the env var.
        output.Should().NotContain("connectionString");
    }

    [Fact]
    public void Module_template_dbsh_registers_context_as_externally_managed()
    {
        var output = _engine.Render(
            "module/Infrastructure/Module", CatalogModel(engine: "dbsh"));

        output.Should().NotContain("{{");
        output.Should().Contain("AddModuleDatabase<CatalogDbContext>");
        output.Should().Contain(".ExternallyManaged<CatalogDbContext>()");
        output.Should().NotContain("  \n"); // no stray blank line from the conditional
    }

    [Fact]
    public void Module_template_efcore_does_not_register_externally_managed()
    {
        var output = _engine.Render(
            "module/Infrastructure/Module", CatalogModel(engine: "efcore"));

        output.Should().NotContain("{{");
        output.Should().Contain("AddModuleDatabase<CatalogDbContext>");
        output.Should().NotContain("ExternallyManaged");
    }

    [Theory]
    [InlineData("efcore")]
    [InlineData("dbsh")]
    public void Program_template_keeps_startup_migration_call_for_both_engines(string engine)
    {
        var model = new AppModel
        {
            RootNamespace = "MyApp",
            AppName = "MyApp",
            DbProvider = "SQLite",
            Auth = "none",
            MigrationEngine = engine,
        };

        var output = _engine.Render("app/Program", model);
        output.Should().NotContain("{{");
        output.Should().Contain("MigrateModulusDatabasesAsync");
        output.Should().Contain("using Modulus.EntityFrameworkCore.Extensions;");
    }

    // ── Module detection (marker file) ────────────────────────────────

    [Fact]
    public void IsDbshModule_true_when_migration_json_marker_present()
    {
        var dir = TempDir();
        try
        {
            var marker = Path.Combine(dir, "Database", "Config", "migration.json");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "{}");

            MigrateSupport.IsDbshModule(dir).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsDbshModule_false_without_marker()
    {
        var dir = TempDir();
        try
        {
            MigrateSupport.IsDbshModule(dir).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsDbshModule_ignores_legacy_dbsh_toml_marker()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "dbsh.toml"), "[migrations]");

            // Clean break: only the v2 Database/Config/migration.json layout counts.
            MigrateSupport.IsDbshModule(dir).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Engine inheritance for new modules ────────────────────────────

    private static string CreateModuleProject(string root, string moduleName, bool dbsh)
    {
        var moduleNs = $"MyApp.Modules.{moduleName}";
        var infraDir = Path.Combine(root, "src", "Modules", moduleNs, $"{moduleNs}.Infrastructure");
        Directory.CreateDirectory(infraDir);
        var csproj = Path.Combine(infraDir, $"{moduleNs}.Infrastructure.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        if (dbsh)
        {
            var marker = Path.Combine(infraDir, "Database", "Config", "migration.json");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "{}");
        }
        return csproj;
    }

    [Fact]
    public void DetectEngine_defaults_to_efcore_without_modules()
    {
        var root = TempDir();
        try
        {
            MigrateSupport.DetectEngine(root).Should().Be("efcore");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DetectEngine_inherits_dbsh_when_all_modules_use_dbsh()
    {
        var root = TempDir();
        try
        {
            CreateModuleProject(root, "Catalog", dbsh: true);
            CreateModuleProject(root, "Orders", dbsh: true);

            MigrateSupport.DetectEngine(root).Should().Be("dbsh");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DetectEngine_defaults_to_efcore_for_ef_or_mixed_apps(bool first, bool second)
    {
        var root = TempDir();
        try
        {
            CreateModuleProject(root, "Catalog", dbsh: first);
            CreateModuleProject(root, "Orders", dbsh: second);

            MigrateSupport.DetectEngine(root).Should().Be("efcore");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── Scaffolding (GenerateModule) ──────────────────────────────────

    [Fact]
    public void GenerateModule_dbsh_writes_dbsh_artifacts_and_keeps_factory()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            var infraDir = Path.Combine(dir, "MyApp.Modules.Catalog.Infrastructure");
            new NewAppCommand().GenerateModule(dir, CatalogModel(engine: "dbsh"));

            File.Exists(Path.Combine(infraDir, "Database", "Config", "migration.json"))
                .Should().BeTrue("dbsh config must be generated");
            File.Exists(Path.Combine(infraDir, "Database", "Config", "environments", "local.json"))
                .Should().BeTrue("dev connection override must be generated (dbsh's default environment is 'local')");
            File.Exists(Path.Combine(infraDir, "Database", "Migrations", ".gitkeep"))
                .Should().BeTrue("migrations folder must exist");
            File.Exists(Path.Combine(infraDir, "CatalogDbContextFactory.cs"))
                .Should().BeTrue("design-time factory is kept for the EF-script bootstrap workflow");

            var moduleCs = File.ReadAllText(Path.Combine(infraDir, "CatalogModule.cs"));
            moduleCs.Should().Contain(".ExternallyManaged<CatalogDbContext>()");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateModule_efcore_writes_no_dbsh_artifacts()
    {
        Ux.Reset();
        var dir = TempDir();
        try
        {
            var infraDir = Path.Combine(dir, "MyApp.Modules.Catalog.Infrastructure");
            new NewAppCommand().GenerateModule(dir, CatalogModel(engine: "efcore"));

            Directory.Exists(Path.Combine(infraDir, "Database")).Should().BeFalse();

            var moduleCs = File.ReadAllText(Path.Combine(infraDir, "CatalogModule.cs"));
            moduleCs.Should().NotContain("ExternallyManaged");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
