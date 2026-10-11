using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>modulus app --multi-tenancy</c>: company = tenant. Resolution with membership checks, the tenant store module,
/// tenant-owned entities and the generated isolation tests.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MultiTenancyTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(bool multiTenancy = true) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = "openiddict",
        Kind = AppKind.Api,
        MultiTenancy = multiTenancy,
    };

    [Fact]
    public void The_option_needs_an_api_host_with_the_local_token_server()
    {
        NewAppCommand.ResolveMultiTenancy(true, AppKind.Api, "openiddict").Should().BeTrue();
        NewAppCommand.ResolveMultiTenancy(false, AppKind.WebAppApi, "none").Should().BeFalse();

        var split = () => NewAppCommand.ResolveMultiTenancy(true, AppKind.WebAppApi, "openiddict");
        var external = () => NewAppCommand.ResolveMultiTenancy(true, AppKind.Api, "keycloak");

        split.Should().Throw<ArgumentException>().WithMessage("*--kind api*");
        external.Should().Throw<ArgumentException>().WithMessage("*--auth openiddict*");
    }

    [Fact]
    public void Program_resolves_the_company_after_authentication_and_seeds_the_default_company()
    {
        var program = _engine.Render("app/Program", Model());

        program.Should().Contain("modules.AddModule<TenancyModule>();")
            .And.Contain(".UseJwtClaimResolver()")
            .And.Contain(".UseHeaderResolver()")
            .And.Contain(".RequireMembership());")
            .And.Contain("builder.Services.AddModulusSecurityContext();")
            .And.Contain("await app.Services.MigrateTenantStoreAsync(")
            .And.Contain("await app.Services.SeedTenancyAsync(app.Environment);")
            .And.Contain("using Shop.Modules.Tenancy.Infrastructure;");

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var tenancy = program.IndexOf("app.UseMultiTenancy();", StringComparison.Ordinal);
        var context = program.IndexOf("app.UseModulusSecurityContext();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        authentication.Should().BePositive();
        tenancy.Should().BeGreaterThan(authentication);
        context.Should().BeGreaterThan(tenancy);
        authorization.Should().BeGreaterThan(context);

        program.IndexOf("MigrateTenantStoreAsync", StringComparison.Ordinal)
            .Should().BeLessThan(program.IndexOf("MigrateModulusDatabasesAsync", StringComparison.Ordinal));
        program.IndexOf("SeedTenancyAsync", StringComparison.Ordinal)
            .Should().BeGreaterThan(program.IndexOf("SeedIdentityAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_option_nothing_tenant_specific_is_generated()
    {
        var model = Model(multiTenancy: false);

        _engine.Render("app/Program", model).Should().NotContain("AddMultiTenancy").And.NotContain("TenancyModule");
        _engine.Render("app/AppTests", model).Should().NotContain("TenantIsolationTests").And.NotContain("ForeignTenantId");
        _engine.Render("app/api.csproj", model).Should().NotContain("Tenancy");
        var settings = _engine.Render("app/appsettings.json", model);
        settings.Should().NotContain("IsolateTenants");
        JsonDocument.Parse(settings, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
    }

    [Fact]
    public void Settings_carry_the_tenant_store_and_isolated_storage()
    {
        var settings = _engine.Render("app/appsettings.json", Model());

        using var json = JsonDocument.Parse(settings, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = json.RootElement;
        root.GetProperty("ConnectionStrings").GetProperty("Tenancy").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("Tenancy").GetProperty("Seed").GetProperty("DefaultTenant").GetString().Should().Be("default");
        root.GetProperty("Storage").GetProperty("IsolateTenants").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void The_api_project_references_the_tenant_store_module()
    {
        _engine.Render("app/api.csproj", Model()).Should()
            .Contain("Shop.Modules.Tenancy.Infrastructure.csproj");
    }

    [Fact]
    public void Tenant_store_module_templates_render()
    {
        var model = Model();

        _engine.Render("tenancy/TenancyModule", model).Should().Contain("AddEfCoreTenantStore(")
            .And.Contain("\"Tenancy\"");
        _engine.Render("tenancy/TenantStoreDbContextFactory", model).Should().Contain("TENANCY_CONNECTION");
        _engine.Render("tenancy/TenancySeeding", model).Should().Contain("SeedTenancyAsync")
            .And.Contain("AddMemberAsync");
        _engine.Render("tenancy/infrastructure.csproj", model).Should()
            .Contain("Cobytelabs.Modulus.MultiTenancy.EntityFrameworkCore");
    }

    [Fact]
    public void Generated_tests_pin_a_company_probe_a_foreign_one_and_assert_isolation()
    {
        var tests = _engine.Render("app/AppTests", Model());

        tests.Should().Contain("tenantId: company, pinTenant: true")
            .And.Contain("new SecurityProbeOptions { ForeignTenantId = foreign }")
            .And.Contain("AssertTenantIsolationAsync<CatalogDbContext, Product>")
            .And.Contain("internal static class Companies")
            .And.Contain("using Modulus.MultiTenancy.EntityFrameworkCore;");
    }

    [Fact]
    public void A_tenant_owned_entity_implements_IHasTenantId()
    {
        var model = new ModuleModel
        {
            RootNamespace = "Shop",
            ModuleNamespace = "Shop.Modules.Catalog",
            ModuleName = "Catalog",
            EntityName = "Product",
            EntityNameLower = "product",
            RouteName = "products",
        };

        _engine.Render("module/Domain/Entity", model).Should().NotContain("IHasTenantId");

        model.MultiTenant = true;
        _engine.Render("module/Domain/Entity", model).Should().Contain(", IHasTenantId")
            .And.Contain("public Guid TenantId { get; set; }");
        _engine.Render("module/Tests/TenantIsolationTests", model).Should()
            .Contain("public sealed class ProductTenantIsolationTests")
            .And.Contain("AssertTenantIsolationAsync<CatalogDbContext, Product>");
    }

    [Fact]
    public void Generate_crud_detects_a_multi_tenant_host_by_its_Program_cs()
    {
        var dir = Directory.CreateTempSubdirectory("modulus-mt-");
        try
        {
            var program = Path.Combine(dir.FullName, "Program.cs");
            File.WriteAllText(program, _engine.Render("app/Program", Model()));
            GenerateCrudCommand.IsMultiTenantHost(program).Should().BeTrue();

            File.WriteAllText(program, _engine.Render("app/Program", Model(multiTenancy: false)));
            GenerateCrudCommand.IsMultiTenantHost(program).Should().BeFalse();
            GenerateCrudCommand.IsMultiTenantHost(null).Should().BeFalse();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
