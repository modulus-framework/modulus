using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Modulus.UI;
using Modulus.UI.Theming;
using Modulus.UI.Theming.Tabler;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

[Trait("Category", "Unit")]
public sealed class TablerThemeTests
{
    private const string Base = "/_content/Pkg";

    private const string CoreBase = "/_content/CorePkg";

    private static TablerTheme NewTheme() => new(Base, CoreBase);

    [Fact]
    public void Name_matches_the_default_ThemeOptions_name()
    {
        NewTheme().Name.Should().Be(new ThemeOptions().Name).And.Be("Tabler");
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Account")]
    [InlineData("Empty")]
    [InlineData("Public")]
    [InlineData("application")]
    public void GetLayout_resolves_every_standard_layout_to_a_compiled_view_path(string name)
    {
        var path = NewTheme().GetLayout(name);

        path.Should().StartWith("/Themes/Tabler/Layouts/").And.EndWith(".cshtml");
        path.Should().Contain(StandardLayouts.All.Single(l => l.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void GetLayout_throws_for_unknown_layouts_listing_the_available_ones()
    {
        var act = () => NewTheme().GetLayout("Kiosk");

        act.Should().Throw<ArgumentException>().WithMessage("*Kiosk*Application*");
    }

    [Fact]
    public void Styles_load_tabler_first_then_the_token_layer()
    {
        NewTheme().Styles.Select(s => s.Path).Should().Equal(
            $"{Base}/tabler/vendor/tabler.min.css",
            $"{Base}/tabler/css/modulus.css");
    }

    [Fact]
    public void Scripts_load_htmx_extensions_components_and_the_runtime_before_alpine()
    {
        NewTheme().Scripts.Select(s => s.Path).Should().Equal(
            $"{Base}/tabler/vendor/htmx.min.js",
            $"{Base}/tabler/vendor/idiomorph-ext.min.js",
            $"{Base}/tabler/vendor/tabler.min.js",
            $"{CoreBase}/modulus-ui/alpine-components.js",
            $"{Base}/tabler/js/modulus.js",
            $"{Base}/tabler/vendor/alpine.csp.min.js");
    }

    [Fact]
    public void Only_the_alpine_script_is_treated_as_alpine()
    {
        var scripts = NewTheme().Scripts;
        var alpine = scripts.Where(s => TablerAssets.IsAlpine(s.Path)).ToList();

        alpine.Should().ContainSingle().Which.Path.Should().EndWith("alpine.csp.min.js");
        alpine.Single().Should().BeSameAs(scripts[^1], "Alpine must start after every script that registers components");
    }

    [Fact]
    public void AddTablerTheme_registers_the_theme_and_manifest_and_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(new FakeEnvironment());

        services.AddTablerTheme();
        services.AddTablerTheme();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ITheme>().Should().ContainSingle().Which.Should().BeOfType<TablerTheme>();
        provider.GetRequiredService<IThemeAccessor>().Current.Name.Should().Be("Tabler");
        provider.GetRequiredService<IUiNavigationRegistry>().GetModules()
            .Should().ContainSingle().Which.Id.Should().Be("Modulus.UI.Theme.Tabler");
    }

    [Fact]
    public void AddTablerTheme_seeds_identity_layout_mappings_and_binds_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modulus:Ui:Theme:ColorMode"] = "dark",
                ["Modulus:Ui:Theme:Layouts:Admin"] = "Application",
                ["Modulus:Ui:Branding:AppName"] = "Acme",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(new FakeEnvironment());

        services.AddTablerTheme(configuration);

        using var provider = services.BuildServiceProvider();
        var theme = provider.GetRequiredService<IOptions<ThemeOptions>>().Value;
        theme.ColorMode.Should().Be("dark");
        theme.ResolveLayout("Admin").Should().Be("Application");
        theme.ResolveLayout("Account").Should().Be("Account");
        provider.GetRequiredService<IOptions<UiOptions>>().Value.Branding.AppName.Should().Be("Acme");
    }

    [Fact]
    public void ContentBase_prefers_the_directory_that_carries_the_theme_marker()
    {
        using var root = new TempWebRoot();
        root.AddFile("_content/Modulus.UI.Core/modulus-ui/modulus-ui.js");
        root.AddFile("_content/Cobytelabs.Modulus.UI.Theme.Tabler/tabler/js/modulus.js");

        TablerAssets.ContentBase(root.Provider).Should().Be("/_content/Cobytelabs.Modulus.UI.Theme.Tabler");
    }

    [Fact]
    public void ContentBase_falls_back_to_the_assembly_path_when_nothing_is_registered()
    {
        using var root = new TempWebRoot();

        TablerAssets.ContentBase(root.Provider).Should().Be("/_content/Modulus.UI.Theme.Tabler");
    }

    [Fact]
    public void Every_asset_the_theme_references_ships_in_the_owning_package_wwwroot()
    {
        var tablerRoot = FindWwwroot("Modulus.UI.Theme.Tabler");
        var coreRoot = FindWwwroot("Modulus.UI.Core");
        var theme = NewTheme();

        foreach (var asset in theme.Styles.Concat(theme.Scripts))
        {
            var (root, relative) = asset.Path.StartsWith(CoreBase + "/", StringComparison.Ordinal)
                ? (coreRoot, asset.Path[(CoreBase.Length + 1)..])
                : (tablerRoot, asset.Path[(Base.Length + 1)..]);

            File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"'{asset.Path}' is referenced by TablerTheme but missing from {root}");
        }
    }

    private static string FindWwwroot(string project)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ui", project, "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate src/ui/{project}/wwwroot above the test output.");
    }

    private sealed class TempWebRoot : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("tabler-webroot-").FullName;

        public IFileProvider Provider { get; }

        public TempWebRoot() => Provider = new PhysicalFileProvider(_path);

        public void AddFile(string relative)
        {
            var full = Path.Combine(_path, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, string.Empty);
        }

        public void Dispose()
        {
            (Provider as IDisposable)?.Dispose();
            Directory.Delete(_path, recursive: true);
        }
    }

    private sealed class FakeEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
