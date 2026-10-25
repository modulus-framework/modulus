using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>modulus app --framework</c>: the .NET target of a generated app decides the EF Core provider versions, and every
/// project file follows the target of the app it belongs to.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class FrameworkTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-framework-" + Guid.NewGuid().ToString("N"));

    public FrameworkTargetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("SqlServer", "net8.0", "8.0.32")]
    [InlineData("PostgreSQL", "net8.0", "8.0.11")]
    [InlineData("MySQL", "net8.0", "8.0.31")]
    [InlineData("SQLite", "net8.0", "8.0.32")]
    [InlineData("SqlServer", "net10.0", "10.0.13")]
    [InlineData("PostgreSQL", "net10.0", "10.0.3")]
    [InlineData("MySQL", "net10.0", "10.0.12")]
    [InlineData("SQLite", "net10.0", "10.0.13")]
    public void The_provider_version_follows_the_target(string provider, string framework, string version)
        => DbProviderInfo.Version(provider, framework).Should().Be(version);

    [Theory]
    [InlineData("SqlServer", "UseSqlServer")]
    [InlineData("PostgreSQL", "UseNpgsql")]
    [InlineData("MySQL", "UseMySQL")]
    [InlineData("SQLite", "UseSqlite")]
    public void The_options_method_is_the_one_each_provider_package_exposes(string provider, string method)
        => DbProviderInfo.UseMethod(provider).Should().Be(method);

    [Fact]
    public void Without_a_target_the_provider_version_is_the_default_target()
        => DbProviderInfo.Version("PostgreSQL").Should().Be(DbProviderInfo.Version("PostgreSQL", AppFramework.Default));

    [Fact]
    public void The_framework_of_an_app_is_read_from_its_build_props_for_any_file_below_it()
    {
        var app = Path.Combine(_root, "Shop");
        Directory.CreateDirectory(Path.Combine(app, "src", "Modules", "Catalog"));
        File.WriteAllText(Path.Combine(app, "Directory.Build.props"), "<Project>\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        AppFramework.Resolve(Path.Combine(app, "src", "Modules", "Catalog", "Catalog.csproj")).Should().Be("net8.0");
    }

    [Fact]
    public void A_path_outside_any_app_gets_the_default_target()
    {
        var loose = Path.Combine(_root, "not-an-app", "file.csproj");

        AppFramework.Resolve(loose).Should().Be(AppFramework.Default);
    }

    [Fact]
    public void The_app_props_for_net8_name_net8_and_the_default_names_net10()
    {
        var net8 = new AppModel { RootNamespace = "Shop", AppName = "Shop", TargetFramework = "net8.0" };
        var net10 = new AppModel { RootNamespace = "Shop", AppName = "Shop" };

        var engine = new TemplateEngine();
        engine.Render("app/Directory.Build.props", net8).Should().Contain("<TargetFramework>net8.0</TargetFramework>");
        engine.Render("app/Directory.Build.props", net10).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
    }
}
