using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The .NET target of a generated app: Modulus targets .NET 10 only, so the EF Core provider versions are the 10.0 ones, and
/// an app that still targets an older framework is refused rather than silently regenerated.
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
    [InlineData("SqlServer", "10.0.13")]
    [InlineData("PostgreSQL", "10.0.3")]
    [InlineData("MySQL", "10.0.12")]
    [InlineData("SQLite", "10.0.13")]
    public void The_provider_version_is_the_net10_version(string provider, string version)
        => DbProviderInfo.Version(provider).Should().Be(version);

    [Theory]
    [InlineData("SqlServer", "UseSqlServer")]
    [InlineData("PostgreSQL", "UseNpgsql")]
    [InlineData("MySQL", "UseMySQL")]
    [InlineData("SQLite", "UseSqlite")]
    public void The_options_method_is_the_one_each_provider_package_exposes(string provider, string method)
        => DbProviderInfo.UseMethod(provider).Should().Be(method);

    [Fact]
    public void The_framework_of_an_app_is_read_from_its_build_props_for_any_file_below_it()
    {
        var app = Path.Combine(_root, "Shop");
        Directory.CreateDirectory(Path.Combine(app, "src", "Modules", "Catalog"));
        File.WriteAllText(Path.Combine(app, "Directory.Build.props"), "<Project>\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        AppFramework.Resolve(Path.Combine(app, "src", "Modules", "Catalog", "Catalog.csproj")).Should().Be("net10.0");
    }

    [Fact]
    public void An_app_that_targets_another_framework_is_refused_with_the_migration_step()
    {
        var app = Path.Combine(_root, "Old");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "Directory.Build.props"), "<Project>\n  <PropertyGroup>\n    <TargetFramework>net9.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        var act = () => AppFramework.Resolve(Path.Combine(app, "Old.csproj"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*net9.0*net10.0*");
    }

    [Fact]
    public void A_path_outside_any_app_gets_the_default_target()
    {
        var loose = Path.Combine(_root, "not-an-app", "file.csproj");

        AppFramework.Resolve(loose).Should().Be(AppFramework.Default);
    }

    [Fact]
    public void The_app_props_name_net10()
    {
        var model = new AppModel { RootNamespace = "Shop", AppName = "Shop" };

        new TemplateEngine().Render("app/Directory.Build.props", model).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
    }

    [Fact]
    public void A_new_app_is_pinned_to_the_net10_sdk_band()
    {
        new TemplateEngine().Render("app/global.json", new { sdk_version = AppFramework.SdkVersion })
            .Should().Contain("\"version\": \"10.0.").And.Contain("\"rollForward\": \"latestFeature\"");
    }
}
