using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The text edits <c>modulus add-audit-store</c> makes (<see cref="AuditStoreWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class AuditStoreWiringTests
{
    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddModulus(builder.Configuration, modules =>\n{\n    modules.AddModule<CatalogModule>();\n});\n" +
        "var app = builder.Build();\napp.Run();\n";

    [Fact]
    public void Program_registers_the_module_and_the_security_audit_once()
    {
        var once = AuditStoreWiring.EnsureApiProgram(Program, "Shop.Modules.Audit");

        once.Should().Contain("    modules.AddModule<AuditModule>();\n});")
            .And.Contain("builder.Services.AddModulusSecurityAudit(builder.Configuration);")
            .And.Contain("using Modulus.AuditLogging.Security;")
            .And.Contain("using Shop.Modules.Audit.Infrastructure;");
        once.IndexOf("AddModulusSecurityAudit(", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        AuditStoreWiring.EnsureApiProgram(once, "Shop.Modules.Audit").Should().Be(once);
    }

    [Fact]
    public void An_existing_security_audit_registration_is_kept()
    {
        var program = Program.Replace("var app", "builder.Services.AddModulusSecurityAudit(builder.Configuration);\nvar app", StringComparison.Ordinal);

        var result = AuditStoreWiring.EnsureApiProgram(program, "Shop.Modules.Audit");

        result.Split("AddModulusSecurityAudit(").Length.Should().Be(2);
    }

    [Fact]
    public void Program_without_anchors_is_left_alone()
        => AuditStoreWiring.EnsureApiProgram("var x = 1;\n", "Shop.Modules.Audit").Should().Be("var x = 1;\n");

    [Fact]
    public void Development_settings_anchor_the_chain_heads_unless_a_security_section_exists()
    {
        var added = AuditStoreWiring.EnsureSettings("{\n  \"Logging\": {}\n}\n", "Development", null);
        WebhooksWiringTests.Parse(added).GetProperty("Security").GetProperty("Audit").GetProperty("AnchorFile").GetString()
            .Should().Be(AuditStoreWiring.DevelopmentAnchorFile);

        const string existing = "{\n  \"Security\": { \"Guard\": { \"Enabled\": true } }\n}\n";
        AuditStoreWiring.EnsureSettings(existing, "Development", null).Should().Be(existing);
    }
}

/// <summary><c>modulus add-audit-store</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class AuditStoreCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-auditcmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string Infra = "src/Modules/Shop.Modules.Audit/Shop.Modules.Audit.Infrastructure";

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

    private static int Run(string dir) => new AddAuditStoreCommand().ExecuteCore(new AddAuditStoreCommand.Settings(), dir);

    [Fact]
    public void Adds_the_store_module_host_wiring_and_settings()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        Read($"{Infra}/Shop.Modules.Audit.Infrastructure.csproj").Should().Contain("Cobytelabs.Modulus.AuditLogging.EntityFrameworkCore");
        Read($"{Infra}/AppAuditDbContext.cs").Should().Contain(": ModulusAuditDbContext(options)");
        Read($"{Infra}/AppAuditDbContextFactory.cs").Should().Contain("AUDIT_CONNECTION");
        Read($"{Infra}/AuditModule.cs").Should().Contain("AddModulusAuditStore<AppAuditDbContext>()");
        Read("Shop.slnx").Should().Contain("Shop.Modules.Audit.Infrastructure.csproj");
        Read("src/API/Shop.Api/Shop.Api.csproj").Should().Contain("Shop.Modules.Audit.Infrastructure.csproj");

        var program = Read("src/API/Shop.Api/Program.cs");
        program.Should().Contain("modules.AddModule<AuditModule>();");
        program.Split("AddModulusSecurityAudit(").Length.Should().Be(2, "the guarded template already records security events");

        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.json"))
            .GetProperty("ConnectionStrings").GetProperty("Audit").GetString().Should().Be("Data Source=audit.db");
        WebhooksWiringTests.Parse(Read("src/API/Shop.Api/appsettings.Development.json"))
            .GetProperty("Security").GetProperty("Audit").GetProperty("AnchorFile").GetString().Should().Be("audit-anchors.jsonl");
    }

    [Fact]
    public void A_host_without_the_guard_gets_the_security_audit_too()
    {
        GenerateApp(auth: "none");

        Run(AppDir).Should().Be(0);

        Read("src/API/Shop.Api/Program.cs").Should().Contain("builder.Services.AddModulusSecurityAudit(builder.Configuration);");
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
