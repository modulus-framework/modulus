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
        NewAppCommand.ResolveMultiTenancy(true, "openiddict").Should().BeTrue();
        NewAppCommand.ResolveMultiTenancy(false, "none").Should().BeFalse();
        var external = () => NewAppCommand.ResolveMultiTenancy(true, "keycloak");

        external.Should().Throw<ArgumentException>().WithMessage("*--auth openiddict*");
    }

    [Fact]
    public void Program_resolves_the_company_after_authentication_and_seeds_the_default_company()
    {
        var program = ApiHostSource.Render(_engine, Model());

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

        ApiHostSource.Render(_engine, model).Should().NotContain("AddMultiTenancy").And.NotContain("TenancyModule");
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
            .Contain("Modulus.MultiTenancy.EntityFrameworkCore");
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
            File.WriteAllText(program, ApiHostSource.Render(_engine, Model()));
            GenerateCrudCommand.IsMultiTenantHost(program).Should().BeTrue();

            File.WriteAllText(program, ApiHostSource.Render(_engine, Model(multiTenancy: false)));
            GenerateCrudCommand.IsMultiTenantHost(program).Should().BeFalse();
            GenerateCrudCommand.IsMultiTenantHost(null).Should().BeFalse();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_person_in_exactly_one_company_acts_in_it_and_the_gateway_reads_the_choice_of_the_request()
    {
        var model = Model();

        var middleware = _engine.Render("app/DefaultCompany.Web", model);
        middleware.Should().Contain("class DefaultCompanyMiddleware").And.Contain("memberships is not [{ } only]")
            .And.Contain("BffGatewayHandler.SelectedCompanyItem").And.Contain("BffGatewayHandler.CompanyCookie");
        _engine.Render("app/PipelineSetup.Web", model).Should().Contain("app.UseMiddleware<DefaultCompanyMiddleware>()");
        _engine.Render("app/PipelineSetup.Web", Model(multiTenancy: false)).Should().NotContain("DefaultCompanyMiddleware");
        _engine.Render("app/BffGatewayHandler.Web", model).Should().Contain("SelectedCompanyItem").And.Contain("CompanyOf(context)");
        middleware.Should().Contain("catch (HttpRequestException)", "an unreachable API must not turn the page the person asked for into a 500");
    }

    [Fact]
    public void The_company_administration_page_calls_the_tenant_admin_routes()
    {
        var model = Model();

        _engine.Render("app/CompanyAdminApiClient.Web", model).Should().Contain("/api/tenancy/tenants")
            .And.Contain("/status").And.Contain("/seats").And.Contain("/members").And.Contain("/parent");
        _engine.Render("app/CompanyAdminModel.Web", model).Should().Contain("class CompaniesModel")
            .And.Contain("tenants:manage");
        _engine.Render("app/CompanyAdmin.Web.cshtml", model).Should()
            .Contain("@model Shop.Web.Pages.Admin.CompaniesModel");
    }

    [Fact]
    public void The_short_name_pattern_is_a_valid_regular_expression_in_browsers()
    {
        // Chrome compiles pattern attributes with the v flag, where a hyphen inside a character class must be escaped.
        _engine.Render("app/CompanyAdmin.Web.cshtml", Model()).Should().Contain("pattern=\"[a-z0-9][a-z0-9\\-]*\"")
            .And.NotContain("[a-z0-9-]*");
    }

    [Fact]
    public void The_business_audit_page_searches_the_company_entries_and_pages_by_number()
    {
        var model = Model();

        _engine.Render("app/BusinessAuditApiClient.Web", model).Should().Contain("/api/audit/business")
            .And.Contain("pageSize");
        var page = _engine.Render("app/BusinessAuditModel.Web", model);
        page.Should().Contain("PageNumber").And.Contain("audit:view");
        _engine.Render("app/BusinessAudit.Web.cshtml", model).Should()
            .Contain("@model Shop.Web.Pages.Admin.BusinessAuditModel");
    }

    [Fact]
    public void The_admin_clients_the_pages_resolve_are_registered()
    {
        var model = Model();

        var registrations = _engine.Render("app/ApiClientExtensions.Web", model);

        registrations.Should().Contain("AddBffGatewayClient<AuditApiClient>()")
            .And.Contain("AddBffGatewayClient<BusinessAuditApiClient>()")
            .And.Contain("AddBffGatewayClient<RoleAdminApiClient>()")
            .And.Contain("AddBffGatewayClient<CompanyAdminApiClient>()");
    }

    [Fact]
    public void The_branch_picker_reaches_the_api_through_the_gateway_as_X_Branch_Id()
    {
        var model = Model();

        _engine.Render("app/BranchApiClient.Web", model).Should().Contain("/api/org/branches");
        _engine.Render("app/BranchModel.Web", model).Should().Contain("BffGatewayHandler.BranchCookie")
            .And.Contain("Guid.TryParse");
        _engine.Render("app/Branch.Web.cshtml", model).Should().Contain("@model Shop.Web.Pages.BranchModel");
        _engine.Render("app/BffGatewayHandler.Web", model).Should().Contain("\"X-Branch-Id\"")
            .And.Contain("\"modulus.branch\"");
    }

    [Fact]
    public void The_settings_page_offers_a_branch_scope_in_multi_company_apps_and_says_when_no_branch_is_chosen()
    {
        var page = _engine.Render("app/Settings.Web.cshtml", Model());

        page.Should().Contain("value=\"branch\"").And.Contain("Current branch")
            .And.Contain("Model.HasBranch");
        _engine.Render("app/SettingsModel.Web", Model()).Should().Contain("HasChosenBranch")
            .And.Contain("BffGatewayHandler.BranchCookie");
        _engine.Render("app/Settings.Web.cshtml", Model(multiTenancy: false)).Should().NotContain("value=\"branch\"");
    }

    [Fact]
    public void The_company_audit_summary_page_shows_six_fields_and_calls_the_summary_route()
    {
        var model = Model();

        _engine.Render("app/CompanyAuditApiClient.Web", model).Should().Contain("/api/audit/company-summary");
        var page = _engine.Render("app/CompanyAuditModel.Web", model);
        page.Should().Contain("audit:company-summary:view").And.Contain("CompanyAuditRow");
        _engine.Render("app/CompanyAudit.Web.cshtml", model).Should()
            .Contain("@model Shop.Web.Pages.Admin.CompanyAuditModel")
            .And.Contain("row.ActorName");
    }
}
