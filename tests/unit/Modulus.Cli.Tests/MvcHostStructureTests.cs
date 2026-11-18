using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The MVC Web host is modular: the account pages are an area (controllers, view models, views), not Razor Pages.</summary>
[Trait("Category", "Unit")]
public sealed class MvcHostStructureTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(string engine) => new()
    {
        RootNamespace = "Acme",
        AppName = "Acme",
        Kind = AppKind.WebAppApi,
        Auth = "openiddict",
        UiEngine = engine,
        UseUiFramework = true,
    };

    [Fact]
    public void The_mvc_host_registers_controllers_with_views_and_no_razor_pages()
    {
        var source = WebHostSource.Render(_engine, Model("mvc"));

        source.Should().Contain("AddControllersWithViews()")
            .And.NotContain("AddRazorPages()").And.NotContain("MapRazorPages()")
            .And.NotContain("RazorPagesOptions").And.Contain("app.MapControllers();");
    }

    [Fact]
    public void The_razor_pages_host_keeps_its_page_conventions()
    {
        var source = WebHostSource.Render(_engine, Model("razor-pages"));

        source.Should().Contain("AddRazorPages()").And.Contain("MapRazorPages()")
            .And.Contain("AllowAnonymousToFolder(\"/Account\")");
    }

    [Theory]
    [InlineData("app/mvc/AccountController")]
    [InlineData("app/mvc/PasswordController")]
    [InlineData("app/mvc/PasskeysController")]
    [InlineData("app/mvc/ErrorController")]
    public void The_account_controllers_are_thin_open_or_authorized_and_in_the_right_place(string template)
    {
        var text = _engine.Render(template, Model("mvc"));

        var antiforgery = template.EndsWith("ErrorController", StringComparison.Ordinal) ? "[IgnoreAntiforgeryToken]" : "[AutoValidateAntiforgeryToken]";
        text.Should().Contain("namespace Acme.Web.").And.Contain(antiforgery);
        if (template.EndsWith("PasskeysController", StringComparison.Ordinal))
            text.Should().Contain("[Authorize]").And.Contain("[Area(\"Account\")]");
        else
            text.Should().Contain("[AllowAnonymous]").And.Contain("Loosened(");
    }

    [Fact]
    public void The_account_area_keeps_the_urls_of_the_razor_pages_host()
    {
        var account = _engine.Render("app/mvc/AccountController", Model("mvc"));
        var password = _engine.Render("app/mvc/PasswordController", Model("mvc"));

        account.Should().Contain("[Route(\"Account\")]").And.Contain("HttpGet(\"Login\")").And.Contain("HttpPost(\"Logout\")")
            .And.Contain("HttpGet(\"AccessDenied\")");
        password.Should().Contain("HttpGet(\"Account/ForgotPassword\")").And.Contain("HttpGet(\"ResetPassword\")");
    }

    [Fact]
    public void A_host_with_the_bff_sign_in_counts_as_signed_in_whichever_engine_it_uses()
        => UiAccessGates.HasSignIn("builder.Services.AddWebAuthentication(builder.Configuration);").Should().BeTrue();
}

[Trait("Category", "Unit")]
public sealed class FeaturePackageWiringTests
{
    private const string Services = """
        namespace Acme.Api.Configuration;

        internal static class FeatureServicesSetup
        {
            public static WebApplicationBuilder AddApiFeatures(this WebApplicationBuilder builder)
            {
                return builder;
            }
        }
        """;

    [Theory]
    [InlineData("opensearch", "AddModulusOpenSearch(")]
    [InlineData("solr", "AddModulusSolr(")]
    public void Search_is_registered_once_with_its_using(string provider, string call)
    {
        var feature = FeaturePackageWiring.Search(provider);
        var once = FeaturePackageWiring.EnsureServices(Services, feature);

        once.Should().Contain(call).And.Contain("using Modulus.Search;");
        FeaturePackageWiring.EnsureServices(once, feature).Should().Be(once);
    }

    [Theory]
    [InlineData("s3", "Synvro.Modulus.Storage.S3")]
    [InlineData("azure", "Synvro.Modulus.Storage.AzureBlobs")]
    [InlineData("gcs", "Synvro.Modulus.Storage.GoogleCloud")]
    public void Storage_names_its_package(string provider, string package)
        => FeaturePackageWiring.Storage(provider).PackageId.Should().Be(package);

    [Fact]
    public void An_unknown_provider_is_refused()
    {
        var search = () => FeaturePackageWiring.Search("lucene");
        var storage = () => FeaturePackageWiring.Storage("ftp");

        search.Should().Throw<ArgumentException>();
        storage.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Settings_are_added_once_and_a_package_without_settings_leaves_the_file_alone()
    {
        const string json = "{\n  \"Logging\": {}\n}";
        var withSearch = FeaturePackageWiring.EnsureSettings(json, FeaturePackageWiring.Search("opensearch"), development: false);

        withSearch.Should().Contain("\"Search\"").And.Contain("BaseUrl");
        FeaturePackageWiring.EnsureSettings(withSearch, FeaturePackageWiring.Search("opensearch"), development: false).Should().Be(withSearch);
        FeaturePackageWiring.EnsureSettings(json, FeaturePackageWiring.Jobs(), development: false).Should().Be(json);
    }

    [Fact]
    public void Key_vault_adds_the_secrets_core_and_the_store()
    {
        var text = FeaturePackageWiring.EnsureServices(Services, FeaturePackageWiring.KeyVault());

        text.Should().Contain("AddModulusSecrets(").And.Contain("AddAzureKeyVaultSecretStore(");
    }
}

[Trait("Category", "Unit")]
public sealed class IdentityProxyRoutingTests
{
    [Fact]
    public void The_web_bff_forwards_account_and_authorization_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bff-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\n  \"RemoteApis\": [\n    { \"LocalPath\": \"/api\", \"Service\": \"api\" }\n  ]\n}");

            UiHostWiring.EnsureBffAccountRoutes(path).Should().BeTrue();
            UiHostWiring.EnsureBffAccountRoutes(path).Should().BeFalse("a second run changes nothing");

            var text = File.ReadAllText(path);
            text.Should().Contain("\"/account\"").And.Contain("\"/authorization\"").And.Contain("\"/api\"");
            System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("RemoteApis").GetArrayLength().Should().Be(3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_proxy_sends_signed_in_calls_through_the_bff_and_recovery_straight_to_the_api()
    {
        var text = new TemplateEngine().Render("ui/IdentityApiProxy", new { Namespace = "Acme.Web" });

        text.Should().Contain("AddBffGatewayClient<IdentityApiClient>()").And.Contain("IdentityAnonymousApiClient")
            .And.Contain("Bff:BaseUrl").And.NotContain("carries no token");
    }
}

[Trait("Category", "Unit")]
public sealed class StoreModuleTemplatesTests
{
    [Theory]
    [InlineData("Media", "ModulusMediaDbContext", "AddModulusMediaStore")]
    [InlineData("Notifications", "ModulusNotificationsDbContext", "AddModulusNotificationsStore")]
    [InlineData("Localization", "ModulusLocalizationDbContext", "AddModulusEfLocalizationStore")]
    public void Each_store_module_renders_its_context_and_registration(string key, string contextBase, string storeCall)
    {
        var spec = key switch
        {
            "Media" => StoreModuleSpec.Media,
            "Notifications" => StoreModuleSpec.Notifications,
            _ => StoreModuleSpec.Localization,
        };
        var model = new StoreModuleInstaller.StoreModel
        {
            Key = spec.Key,
            Title = spec.Title,
            PackageId = spec.PackageId,
            ContextNamespace = spec.ContextNamespace,
            ContextBase = spec.ContextBase,
            StoreCall = spec.StoreCall,
            StoreNamespace = $"Acme.Modules.{key}",
        };
        var engine = new TemplateEngine();

        engine.Render("store/StoreDbContext", model).Should().Contain($"App{key}DbContext").And.Contain(contextBase);
        engine.Render("store/StoreModule", model).Should().Contain(storeCall).And.Contain($"{key}Module");
        engine.Render("store/StoreDbContextFactory", model).Should().Contain($"{key.ToUpperInvariant()}_CONNECTION");
        engine.Render("store/infrastructure.csproj", model).Should().Contain(spec.PackageId);
    }

    [Fact]
    public void The_connection_string_is_added_once()
    {
        const string json = "{\n  \"ConnectionStrings\": {\n    \"Catalog\": \"x\"\n  }\n}";
        var once = StoreModuleInstaller.EnsureConnection(json, "Media", "Data Source=media.db");

        once.Should().Contain("\"Media\": \"Data Source=media.db\"");
        System.Text.Json.JsonDocument.Parse(once);
        StoreModuleInstaller.EnsureConnection(once, "Media", "Data Source=media.db").Should().Be(once);
    }
}
