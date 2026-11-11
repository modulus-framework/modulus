using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The edit <c>modulus add-authorization-store</c> makes to the host (<see cref="AuthorizationStoreWiring"/>).</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationStoreWiringTests
{
    private const string Old = """
        using Modulus.Authorization.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore;

        builder.Services.AddEfCoreAuthorizationStores(options => options.UseSqlite(builder.Configuration.GetConnectionString("Authorization") ?? throw new InvalidOperationException("ConnectionStrings:Authorization is not configured.")));
        """;

    [Fact]
    public void The_default_registration_becomes_the_modules_call_with_its_using()
    {
        var text = AuthorizationStoreWiring.ReplaceRegistration(Old, "Shop.Modules.Authorization");

        text.Should().Contain("builder.Services.AddAuthorizationStore(builder.Configuration);");
        text.Should().NotContain("AddEfCoreAuthorizationStores(");
        text.Should().StartWith("using Shop.Modules.Authorization.Infrastructure;" + "\n" + "using Modulus.Authorization.EntityFrameworkCore;");
    }

    [Fact]
    public void Running_it_again_changes_nothing()
    {
        var once = AuthorizationStoreWiring.ReplaceRegistration(Old, "Shop.Modules.Authorization");

        AuthorizationStoreWiring.ReplaceRegistration(once, "Shop.Modules.Authorization").Should().Be(once);
        AuthorizationStoreWiring.UsesDefaultStore(once).Should().BeFalse();
    }

    [Fact]
    public void Text_without_the_generated_registration_is_left_alone()
    {
        const string other = "builder.Services.AddEfCoreAuthorizationStores(options => options.UseSqlite(\"custom\"));";

        AuthorizationStoreWiring.ReplaceRegistration(other, "Shop.Modules.Authorization").Should().Be(other);
    }
}

/// <summary><c>modulus add-authorization-store</c> on generated apps.</summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class AuthorizationStoreCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-authcmd-" + Guid.NewGuid().ToString("N"));

    private string AppDir => Path.Combine(_dir, "Shop");

    private const string Infra = "src/Modules/Shop.Modules.Authorization/Shop.Modules.Authorization.Infrastructure";

    private string ProgramPath => Path.Combine(AppDir, "src/API/Shop.Api/Program.cs");

    private void GenerateApp()
    {
        Ux.Quiet = true;
        new NewAppCommand().GenerateAll(AppDir, new AppModel
        {
            RootNamespace = "Shop",
            AppName = "Shop",
            Auth = "openiddict",
            Kind = AppKind.Api,
        });
    }

    private static int Run(string dir) => new AddAuthorizationStoreCommand().ExecuteCore(new AddAuthorizationStoreCommand.Settings(), dir);

    private string HostRegistrationFile() => ApiHostFiles.FileWith(ProgramPath, AuthorizationStoreWiring.OwnStoreMarker);

    [Fact]
    public void An_app_generated_with_the_module_is_left_as_it_is()
    {
        GenerateApp();

        Run(AppDir).Should().Be(0);

        File.Exists(Path.Combine(AppDir, Infra, "AppAuthorizationDbContext.cs")).Should().BeTrue();
        ApiHostFiles.ReadAll(ProgramPath).Should().Contain("AddAuthorizationStore(");
    }

    [Fact]
    public void An_app_from_before_the_module_gets_it_and_running_again_changes_nothing()
    {
        GenerateApp();
        // An app generated before the Authorization module: the default context registered in the host, no module.
        var file = HostRegistrationFile();
        var text = File.ReadAllText(file);
        File.WriteAllText(file, text.Replace(
            "builder.Services.AddAuthorizationStore(builder.Configuration);",
            "builder.Services.AddEfCoreAuthorizationStores(options => options.UseSqlite(builder.Configuration.GetConnectionString(\"Authorization\") ?? throw new InvalidOperationException(\"ConnectionStrings:Authorization is not configured.\")));"));
        Directory.Delete(Path.Combine(AppDir, "src/Modules/Shop.Modules.Authorization"), recursive: true);

        Run(AppDir).Should().Be(0);

        File.Exists(Path.Combine(AppDir, Infra, "AppAuthorizationDbContextFactory.cs")).Should().BeTrue();
        var after = ApiHostFiles.ReadAll(ProgramPath);
        after.Should().Contain("builder.Services.AddAuthorizationStore(builder.Configuration);");
        after.Should().NotContain("AddEfCoreAuthorizationStores(");
        File.ReadAllText(Path.Combine(AppDir, "src/API/Shop.Api/Shop.Api.csproj"))
            .Should().Contain("Shop.Modules.Authorization.Infrastructure.csproj");

        var snapshot = Directory.GetFiles(AppDir, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => (f, File.ReadAllText(f))).ToList();
        Run(AppDir).Should().Be(0);
        Directory.GetFiles(AppDir, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => (f, File.ReadAllText(f))).Should().Equal(snapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
