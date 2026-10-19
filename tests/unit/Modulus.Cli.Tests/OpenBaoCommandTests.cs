using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus add-openbao</c> makes (<see cref="OpenBaoWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class OpenBaoWiringTests
{
    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddModulus(builder.Configuration, modules =>\n{\n    modules.AddModule<CatalogModule>();\n});\n" +
        "var app = builder.Build();\napp.Run();\n";

    [Fact]
    public void Program_gets_the_store_once_and_the_tenant_registration_only_when_asked()
    {
        var once = OpenBaoWiring.EnsureApiServices(Program, tenantDatabases: true);

        once.Should().Contain("builder.Services.AddModulusSecrets(builder.Configuration);")
            .And.Contain("builder.Services.AddOpenBaoSecretStore(builder.Configuration);")
            .And.Contain("builder.Services.AddModulusTenantDatabases(builder.Configuration);")
            .And.Contain("using Modulus.Secrets.OpenBao;");
        OpenBaoWiring.EnsureApiServices(once, tenantDatabases: true).Should().Be(once);

        var plain = OpenBaoWiring.EnsureApiServices(Program, tenantDatabases: false);
        plain.Should().NotContain("AddModulusTenantDatabases(");
    }

    [Fact]
    public void Program_without_an_anchor_is_left_alone()
        => OpenBaoWiring.EnsureApiServices("var x = 1;\n", tenantDatabases: false).Should().Be("var x = 1;\n");

    [Fact]
    public void Production_settings_name_the_kubernetes_login_by_default()
    {
        var json = WebhooksWiringTests.Parse(OpenBaoWiring.EnsureProductionSettings("{\n  \"Logging\": {}\n}\n", "kubernetes"));

        var secrets = json.GetProperty("Secrets");
        secrets.GetProperty("RuntimeReadOnly").GetBoolean().Should().BeTrue();
        var auth = secrets.GetProperty("OpenBao").GetProperty("Auth");
        auth.GetProperty("Method").GetString().Should().Be("Kubernetes");
        auth.GetProperty("Role").GetString().Should().Be("modulus-api");
        secrets.GetProperty("OpenBao").TryGetProperty("AllowInsecureHttp", out _).Should().BeFalse("http is never production");
    }

    [Fact]
    public void Production_settings_with_approle_read_the_ids_from_files()
    {
        var json = WebhooksWiringTests.Parse(OpenBaoWiring.EnsureProductionSettings("{}\n", "approle"));

        var auth = json.GetProperty("Secrets").GetProperty("OpenBao").GetProperty("Auth");
        auth.GetProperty("Method").GetString().Should().Be("AppRole");
        auth.GetProperty("RoleIdPath").GetString().Should().Be("/run/secrets/openbao-role-id");
        auth.GetProperty("SecretIdPath").GetString().Should().Be("/run/secrets/openbao-secret-id");
    }

    [Fact]
    public void Development_settings_use_the_dev_server_over_http_with_the_ignored_id_files()
    {
        var json = WebhooksWiringTests.Parse(OpenBaoWiring.EnsureDevelopmentSettings("{}\n"));

        var openBao = json.GetProperty("Secrets").GetProperty("OpenBao");
        openBao.GetProperty("Address").GetString().Should().Be("http://127.0.0.1:8200");
        openBao.GetProperty("AllowInsecureHttp").GetBoolean().Should().BeTrue();
        openBao.GetProperty("Auth").GetProperty("RoleIdPath").GetString().Should().Be(OpenBaoWiring.DevelopmentRoleIdFile);
    }

    [Fact]
    public void An_existing_secrets_section_is_kept()
    {
        const string existing = "{\n  \"Secrets\": { \"RuntimeReadOnly\": false }\n}\n";

        OpenBaoWiring.EnsureProductionSettings(existing, "kubernetes").Should().Be(existing);
        OpenBaoWiring.EnsureDevelopmentSettings(existing).Should().Be(existing);
    }
}

/// <summary><c>modulus add-openbao</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class AddOpenBaoCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-openbao-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string ApiDir = "src/API/Shop.Api";

    private void GenerateApp(string auth = "openiddict")
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = auth,
            Kind = AppKind.Api,
        });
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private static int Run(string dir, string? auth = null, bool tenantDatabases = false)
        => new AddOpenBaoCommand().ExecuteCore(new AddOpenBaoCommand.Settings { Auth = auth, TenantDatabases = tenantDatabases }, dir);

    [Fact]
    public void Writes_the_policies_bootstrap_package_reference_host_wiring_and_settings()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        Read("deploy/openbao/runtime-read.hcl").Should().Contain("modulus-prod/data/tenant-db/*");
        Read("deploy/openbao/provisioner.hcl").Should().Contain("\"create\", \"update\", \"read\"");
        Read("deploy/openbao/bootstrap-dev.sh").Should().Contain("bao policy write modulus-runtime-read");
        Read("docker-compose.openbao.yml").Should().Contain("openbao/openbao:2.1.0");
        Read($"{ApiDir}/Shop.Api.csproj").Should().Contain("Synvro.Modulus.Secrets.OpenBao");

        ApiHostFiles.ReadAll(Path.Combine(AppDir, ApiDir, "Program.cs")).Should().Contain("AddOpenBaoSecretStore(");

        var production = WebhooksWiringTests.Parse(Read($"{ApiDir}/appsettings.json")).GetProperty("Secrets").GetProperty("OpenBao");
        production.GetProperty("Auth").GetProperty("Method").GetString().Should().Be("Kubernetes");
        var development = WebhooksWiringTests.Parse(Read($"{ApiDir}/appsettings.Development.json")).GetProperty("Secrets").GetProperty("OpenBao");
        development.GetProperty("AllowInsecureHttp").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Tenant_databases_and_approle_are_wired_when_asked()
    {
        GenerateApp();

        Run(AppDir, auth: "approle", tenantDatabases: true).Should().Be(0);

        ApiHostFiles.ReadAll(Path.Combine(AppDir, ApiDir, "Program.cs")).Should().Contain("AddModulusTenantDatabases(");
        var auth = WebhooksWiringTests.Parse(Read($"{ApiDir}/appsettings.json")).GetProperty("Secrets").GetProperty("OpenBao").GetProperty("Auth");
        auth.GetProperty("Method").GetString().Should().Be("AppRole");
    }

    [Fact]
    public void An_unknown_login_is_refused()
    {
        GenerateApp();

        var act = () => Run(AppDir, auth: "password");

        act.Should().Throw<ArgumentException>().WithMessage("*--auth*");
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        GenerateApp();
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
