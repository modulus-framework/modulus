using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>OpenBao Phase 4: the generated provisioning host, the only host with a write identity to OpenBao.</summary>
[Trait("Category", "Unit")]
public sealed class ProvisioningHostTests
{
    private static ProvisioningHost.Model Model(string engine, string auth = "kubernetes")
        => new("Shop", "1.5.0-local.2", engine, auth, "11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" />", "PostgreSQL")]
    [InlineData("<PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" />", "SqlServer")]
    [InlineData("<PackageReference Include=\"MySql.EntityFrameworkCore\" />", "MySQL")]
    [InlineData("<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" />", "SQLite")]
    [InlineData("<PackageReference Include=\"Synvro.Modulus.Core\" />", null)]
    public void The_engine_is_read_from_the_project_packages(string projectText, string? expected)
        => ProvisioningHost.DetectEngine(projectText).Should().Be(expected);

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    [InlineData("MySQL")]
    [InlineData("SQLite")]
    public void Every_engine_the_generator_detects_has_a_provisioning_host(string engine)
        => ProvisioningHost.EnsureSupported(engine);

    [Fact]
    public void An_engine_without_a_probe_client_is_refused_with_the_reason()
    {
        var act = () => ProvisioningHost.EnsureSupported("Oracle");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Oracle*client*");
    }

    [Theory]
    [InlineData("MySQL", "UseMySQL", "mysql", "MySql.Data.MySqlClient", "MySqlConnection", "Microsoft.EntityFrameworkCore")]
    [InlineData("SQLite", "UseSqlite", "sqlite", "Microsoft.Data.Sqlite", "SqliteConnection", "Microsoft.EntityFrameworkCore")]
    public void The_program_probes_and_writes_through_the_engine_that_the_api_uses(
        string engine, string useMethod, string providerKey, string probeNamespace, string probeClass, string extensionsNamespace)
    {
        var model = Model(engine);
        var program = new TemplateEngine().Render("provisioning/Program", model);

        model.UseMethod.Should().Be(useMethod);
        model.ProviderKey.Should().Be(providerKey);
        model.ProbeNamespace.Should().Be(probeNamespace);
        model.ProbeClass.Should().Be(probeClass);
        model.EfExtensionsNamespace.Should().Be(extensionsNamespace);
        program.Should().Contain($"o.{useMethod}(")
            .And.Contain($"AddTenantDatabaseProvider(\"{providerKey}\"")
            .And.Contain($"new {probeClass}(connection)")
            .And.Contain($"using {probeNamespace};")
            .And.Contain($"using {extensionsNamespace};");
    }

    [Fact]
    public void The_program_writes_only_through_the_writer_and_serves_only_the_provisioning_routes()
    {
        var program = new TemplateEngine().Render("provisioning/Program", Model("PostgreSQL"));

        program.Should().Contain("builder.Services.AddOpenBaoSecretWriter(builder.Configuration);")
            .And.Contain("app.MapModulusTenantProvisioning();")
            .And.Contain("builder.Services.AddModulusTenantProvisioningAdmin();")
            .And.Contain("options.TrustPermissionClaims = false")
            .And.Contain("options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();")
            .And.Contain("UseNpgsql(")
            .And.Contain("new NpgsqlConnection(connection)")
            .And.Contain(".Loosen(\"Liveness probe for orchestrators and load balancers; returns no data\")");
        program.Should().NotContain("AllowAnonymous", "an endpoint is opened only with a reviewed reason");
        program.Should().NotContain("AddOpenBaoSecretStore", "the provisioning host is not a runtime reader");
    }

    [Fact]
    public void The_sql_server_program_uses_its_own_client_and_provider_key()
    {
        var program = new TemplateEngine().Render("provisioning/Program", Model("SqlServer"));

        program.Should().Contain("UseSqlServer(").And.Contain("new SqlConnection(connection)")
            .And.Contain("AddTenantDatabaseProvider(\"sqlserver\"");
    }

    [Fact]
    public void Production_settings_turn_the_read_only_guard_off_and_name_the_writer_identity()
    {
        var settings = new TemplateEngine().Render("provisioning/appsettings", Model("PostgreSQL"));

        settings.Should().Contain("\"RuntimeReadOnly\": false")
            .And.Contain("\"Mount\": \"modulus-prod\"")
            .And.Contain("\"Writer\"")
            .And.Contain("\"Role\": \"modulus-provisioner\"");
        settings.Should().NotContain("RoleIdPath", "the Kubernetes login reads a service-account token, not AppRole files");
    }

    [Fact]
    public void Production_approle_settings_name_the_provisioner_secret_files()
    {
        var settings = new TemplateEngine().Render("provisioning/appsettings", Model("PostgreSQL", "approle"));

        settings.Should().Contain("\"RoleIdPath\": \"/run/secrets/openbao-provisioner-role-id\"")
            .And.Contain("\"SecretIdPath\": \"/run/secrets/openbao-provisioner-secret-id\"");
    }

    [Fact]
    public void Development_settings_point_the_writer_at_its_own_files()
    {
        var settings = new TemplateEngine().Render("provisioning/appsettings.Development", Model("PostgreSQL"));

        settings.Should().Contain("\"RuntimeReadOnly\": false")
            .And.Contain("\"RoleIdPath\": \"secrets/openbao-provisioner-role-id\"")
            .And.Contain("\"SecretIdPath\": \"secrets/openbao-provisioner-secret-id\"");
    }

    [Fact]
    public void The_program_reads_grants_from_the_api_authorization_database_and_migrates_the_catalog_it_writes()
    {
        var program = new TemplateEngine().Render("provisioning/Program", Model("PostgreSQL"));

        program.Should().Contain("AddEfCoreAuthorizationStores(o => o.UseNpgsql(builder.Configuration.GetConnectionString(\"Authorization\") ?? throw new InvalidOperationException(\"ConnectionStrings:Authorization is not configured.\")))")
            .And.Contain("MigrateTenantStoreAsync(ensureCreatedIfNoMigrations: !app.Environment.IsProduction())")
            .And.NotContain("// builder.Services.AddEfCoreAuthorizationStores", "the grant store is wired, not a comment");
    }

    [Fact]
    public void The_host_serves_no_company_so_its_tenant_accessor_is_the_host_context()
    {
        var program = new TemplateEngine().Render("provisioning/Program", Model("PostgreSQL"));

        // Multi-tenancy would fail closed on an unresolved request and hide the host-level grants from the grant store.
        program.Should().Contain("TryAddSingleton<Modulus.Core.Abstractions.ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();")
            .And.NotContain("AddMultiTenancy();");
    }

    [Fact]
    public void The_host_keeps_the_catalog_snapshot_and_owns_its_rows_of_it()
    {
        var program = new TemplateEngine().Render("provisioning/Program", Model("PostgreSQL"));

        program.Should().NotContain("RemoveAll<Modulus.Authorization.IPermissionCatalogSnapshotStore>",
            "the catalog is shared, and each host reconciles only its own rows")
            .And.Contain("PermissionCatalogOptions.Owner");
    }

    [Fact]
    public void The_settings_name_the_authorization_database_with_the_same_connection_as_the_api()
    {
        var settings = new TemplateEngine().Render("provisioning/appsettings", Model("PostgreSQL"));

        settings.Should().Contain("\"Authorization\": \"Host=localhost;Database=modulus_authorization;Username=postgres;Password=postgres\"");
    }

    [Fact]
    public void The_sqlite_host_pins_the_patched_native_library_so_it_builds_without_a_known_vulnerability()
    {
        var project = new TemplateEngine().Render("provisioning/provisioning.csproj", Model("SQLite"));

        project.Should().Contain("<PackageReference Include=\"SQLitePCLRaw.bundle_e_sqlite3\" Version=\"3.0.3\" />");
        new TemplateEngine().Render("provisioning/provisioning.csproj", Model("PostgreSQL"))
            .Should().NotContain("SQLitePCLRaw", "only the SQLite host needs the native library");
    }

    [Fact]
    public void The_project_references_the_writer_and_the_engine_client()
    {
        var project = new TemplateEngine().Render("provisioning/provisioning.csproj", Model("SqlServer"));

        project.Should().Contain("Synvro.Modulus.Secrets.OpenBao")
            .And.Contain("Synvro.Modulus.MultiTenancy.EntityFrameworkCore")
            .And.Contain("Microsoft.EntityFrameworkCore.SqlServer")
            .And.Contain("<RootNamespace>Shop.Provisioning</RootNamespace>");
    }
}
