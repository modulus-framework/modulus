using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// A template package older than the CLI's component list (the pinned UI version is published before the CLI learns new names)
/// must not break <c>ui add-component</c>: <c>--all</c> adds what the package ships, and a component named on purpose that the
/// package lacks fails with a message that says what to do.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiAddComponentGatingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gate").FullName;
    private readonly string _ui;
    private readonly string _oldUserRoot = UiTemplatePackage.UserRoot;

    public UiAddComponentGatingTests()
    {
        _ui = Path.Combine(_root, "src", "API", "Demo.Api");
        Directory.CreateDirectory(_ui);
        File.WriteAllText(Path.Combine(_root, "Demo.slnx"), "<Solution/>");
        File.WriteAllText(Path.Combine(_ui, "Demo.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><ModulusAppKind>webapp+api</ModulusAppKind></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_root, ".modulus.json"), """{ "ui_engine": "mvc" }""");

        // A package cache holding only "alert": every other component is "newer than the package".
        UiTemplatePackage.UserRoot = Path.Combine(_root, "home");
        var templates = UiTemplatePackage.CacheDir(UiTemplatePackage.PackageId, UiTemplatePackage.DefaultVersion);
        Directory.CreateDirectory(Path.Combine(templates, "mvc"));
        File.WriteAllText(Path.Combine(templates, "mvc", "alert.cshtml.sbn"), "@* alert *@\n<div class=\"ant-alert\"></div>");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(templates)!, UiTemplatePackage.ManifestFile), "{}");
    }

    public void Dispose()
    {
        UiTemplatePackage.UserRoot = _oldUserRoot;
        Directory.Delete(_root, recursive: true);
    }

    private int Run(params string[] args) => Program.Main([.. args, "-o", _root]);

    private string Component(string name) => Path.Combine(_ui, "Views", "Shared", "Components", name);

    [Fact]
    public void All_adds_what_the_package_has_and_skips_the_rest()
    {
        Run("ui", "add-component", "--all").Should().Be(0);

        File.Exists(Component("_Alert.cshtml")).Should().BeTrue();
        Directory.GetFiles(Path.GetDirectoryName(Component("_Alert.cshtml"))!).Should().HaveCount(1, "no other template exists in this package");
    }

    [Fact]
    public void A_component_named_on_purpose_that_the_package_lacks_fails()
    {
        Run("ui", "add-component", "tag-input").Should().Be(1);

        File.Exists(Component("_TagInput.cshtml")).Should().BeFalse();
    }
}
