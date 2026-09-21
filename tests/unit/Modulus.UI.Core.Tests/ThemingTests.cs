using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.UI;
using Modulus.UI.Theming;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the theme seams: registration, the active-theme accessor, and component view resolution.</summary>
[Trait("Category", "Unit")]
public sealed class ThemingTests
{
    private class FakeTheme(string name) : ITheme
    {
        public string Name => name;

        public string GetLayout(string layoutName) => $"/Themes/{name}/Layouts/{layoutName}.cshtml";

        public IReadOnlyList<ThemeAsset> Styles { get; } = [];

        public IReadOnlyList<ThemeAsset> Scripts { get; } = [];
    }

    private sealed class Acme() : FakeTheme("Acme");

    private sealed class Tabler() : FakeTheme("Tabler");

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ---- AddModulusTheme --------------------------------------------------------

    [Fact]
    public void AddModulusTheme_registers_the_accessor_resolver_and_seeds_identity_layouts()
    {
        var services = new ServiceCollection();
        services.AddModulusTheme<Tabler>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThemeAccessor>().Current.Name.Should().Be("Tabler");
        // Resolving the view resolver needs MVC's view engine; that path is covered by the
        // Tabler render tests. Here we only assert the seam is registered.
        services.Should().ContainSingle(d => d.ServiceType == typeof(IModulusViewResolver)
            && d.ImplementationType == typeof(ModulusViewResolver));
        var options = provider.GetRequiredService<IOptions<ThemeOptions>>().Value;
        options.Layouts.Should().Contain(StandardLayouts.All.Select(l => KeyValuePair.Create(l, l)));
    }

    [Fact]
    public void AddModulusTheme_is_safe_to_call_repeatedly()
    {
        using var provider = Build(s =>
        {
            s.AddModulusTheme<Tabler>();
            s.AddModulusTheme<Tabler>();
        });

        provider.GetServices<ITheme>().Should().ContainSingle();
    }

    [Fact]
    public void AddModulusTheme_keeps_user_layout_mappings_when_seeding_defaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Theme:Layouts:Application"] = "Admin",
            ["Modulus:Ui:Theme:Layouts:Reports"] = "Public",
        }).Build();

        using var provider = Build(s => s.AddModulusTheme<Tabler>(configuration));

        var options = provider.GetRequiredService<IOptions<ThemeOptions>>().Value;
        options.ResolveLayout("Application").Should().Be("Admin");
        options.ResolveLayout("Reports").Should().Be("Public");
        options.ResolveLayout("Account").Should().Be("Account");
    }

    [Fact]
    public void AddModulusTheme_binds_ui_and_theme_sections_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Branding:AppName"] = "Acme",
            ["Modulus:Ui:Branding:FooterText"] = "© Acme",
            ["Modulus:Ui:Features:Boost"] = "false",
            ["Modulus:Ui:Features:GlobalSearch"] = "true",
            ["Modulus:Ui:Theme:Name"] = "Acme",
            ["Modulus:Ui:Theme:ColorMode"] = "dark",
            ["Modulus:Ui:Theme:AllowUserThemeSwitch"] = "true",
        }).Build();

        using var provider = Build(s => s.AddModulusTheme<Acme>(configuration));

        var ui = provider.GetRequiredService<IOptions<UiOptions>>().Value;
        ui.Branding.AppName.Should().Be("Acme");
        ui.Branding.FooterText.Should().Be("© Acme");
        ui.Features.Boost.Should().BeFalse();
        ui.Features.GlobalSearch.Should().BeTrue();
        var theme = provider.GetRequiredService<IOptions<ThemeOptions>>().Value;
        (theme.Name, theme.ColorMode, theme.AllowUserThemeSwitch).Should().Be(("Acme", "dark", true));
    }

    [Fact]
    public void UiOptions_defaults_match_the_documented_shell_behavior()
    {
        var ui = new UiOptions();

        ui.Branding.AppName.Should().Be("Modulus");
        ui.Branding.BrandHref.Should().Be("~/");
        ui.Features.Boost.Should().BeTrue();
        ui.Features.Breadcrumbs.Should().BeTrue();
        ui.Features.Morph.Should().BeFalse();
        ui.Features.GlobalSearch.Should().BeFalse();
        UiOptions.SectionName.Should().Be("Modulus:Ui");
        ThemeOptions.SectionName.Should().Be("Modulus:Ui:Theme");
    }

    // ---- IThemeAccessor ---------------------------------------------------------

    [Fact]
    public void Accessor_selects_the_theme_named_in_options_ignoring_case()
    {
        using var provider = Build(s =>
        {
            s.AddModulusTheme<Tabler>();
            s.AddModulusTheme<Acme>();
            s.Configure<ThemeOptions>(o => o.Name = "acme");
        });

        provider.GetRequiredService<IThemeAccessor>().Current.Name.Should().Be("Acme");
    }

    [Fact]
    public void Accessor_fails_fast_naming_the_available_themes_when_the_configured_one_is_missing()
    {
        using var provider = Build(s =>
        {
            s.AddModulusTheme<Tabler>();
            s.Configure<ThemeOptions>(o => o.Name = "Missing");
        });

        var act = () => provider.GetRequiredService<IThemeAccessor>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Missing'*Available: Tabler*");
    }

    [Fact]
    public void Accessor_explains_how_to_fix_a_host_with_no_theme_registered()
    {
        using var provider = Build(s =>
        {
            s.AddModulusUi();
            s.AddSingleton<IThemeAccessor, ThemeAccessor>();
            s.AddOptions<ThemeOptions>();
        });

        var act = () => provider.GetRequiredService<IThemeAccessor>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*<none*AddTablerTheme*");
    }

    // ---- IModulusViewResolver ---------------------------------------------------

    private const string AppView = "/Views/Shared/Modulus/Card/Default.cshtml";
    private const string ThemeView = "/Themes/Tabler/Views/Card/Default.cshtml";
    private const string DefaultView = "/Views/Shared/Modulus/_Default/Card/Default.cshtml";

    private static IRazorViewEngine Engine(params string[] existing)
    {
        var engine = Substitute.For<IRazorViewEngine>();
        engine.GetView(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(call => ViewEngineResult.NotFound(call.ArgAt<string>(1), []));
        foreach (var path in existing)
        {
            engine.GetView(Arg.Any<string?>(), path, Arg.Any<bool>())
                .Returns(ViewEngineResult.Found(path, Substitute.For<IView>()));
        }

        return engine;
    }

    private static ModulusViewResolver Resolver(IRazorViewEngine engine, string theme = "Tabler")
        => new(engine, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ThemeOptions { Name = theme }));

    [Fact]
    public void Resolve_prefers_the_app_override_then_the_theme_then_the_framework_default()
    {
        Resolver(Engine(AppView, ThemeView, DefaultView)).Resolve("Card", "Default").Should().Be(AppView);
        Resolver(Engine(ThemeView, DefaultView)).Resolve("Card", "Default").Should().Be(ThemeView);
        Resolver(Engine(DefaultView)).Resolve("Card", "Default").Should().Be(DefaultView);
    }

    [Fact]
    public void Resolve_uses_the_active_theme_name_in_the_theme_path()
    {
        var engine = Engine("/Themes/Acme/Views/Card/Default.cshtml");

        Resolver(engine, "Acme").Resolve("Card", "Default").Should().Be("/Themes/Acme/Views/Card/Default.cshtml");
        Resolver(engine, "Tabler").TryResolve("Card", "Default", out _).Should().BeFalse();
    }

    [Fact]
    public void Resolve_throws_listing_every_searched_location_when_nothing_matches()
    {
        var act = () => Resolver(Engine()).Resolve("Card", "Default");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'Default'*'Card'*'Tabler'*{AppView}*{ThemeView}*{DefaultView}*");
    }

    [Fact]
    public void TryResolve_reports_a_miss_without_throwing_and_an_empty_path()
    {
        Resolver(Engine()).TryResolve("Card", "Default", out var path).Should().BeFalse();
        path.Should().BeEmpty();
    }

    [Fact]
    public void Results_are_cached_so_the_view_engine_is_probed_once_per_view()
    {
        var engine = Engine(ThemeView);
        var resolver = Resolver(engine);

        resolver.Resolve("Card", "Default");
        resolver.Resolve("Card", "Default");
        resolver.TryResolve("Card", "Default", out _);

        engine.Received(2).GetView(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<bool>()); // app miss + theme hit, once
    }

    [Fact]
    public void Misses_are_cached_too()
    {
        var engine = Engine();
        var resolver = Resolver(engine);

        resolver.TryResolve("Card", "Default", out _);
        resolver.TryResolve("Card", "Default", out _);

        engine.Received(3).GetView(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void Blank_component_or_view_names_are_rejected()
    {
        var resolver = Resolver(Engine());

        ((Action)(() => resolver.Resolve("", "Default"))).Should().Throw<ArgumentException>();
        ((Action)(() => resolver.Resolve("Card", " "))).Should().Throw<ArgumentException>();
    }

    // ---- Theme.Abstractions contracts ------------------------------------------

    [Fact]
    public void ThemeOptions_ResolveLayout_falls_back_to_the_logical_name()
    {
        var options = new ThemeOptions();
        options.Layouts["Admin"] = "Application";

        options.ResolveLayout("Admin").Should().Be("Application");
        options.ResolveLayout("admin").Should().Be("Application");
        options.ResolveLayout("Print").Should().Be("Print");
        ((Action)(() => options.ResolveLayout(""))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThemeAsset_defers_scripts_by_default_with_no_integrity()
    {
        var asset = new ThemeAsset("/x.js");

        (asset.Defer, asset.Integrity).Should().Be((true, (string?)null));
    }

    [Fact]
    public void UiDesignTokens_CssVar_lowercases_into_the_m_namespace()
    {
        UiDesignTokens.CssVar(UiDesignTokens.Primary).Should().Be("--m-primary");
        UiDesignTokens.CssVar(UiDesignTokens.SidebarWidth).Should().Be("--m-sidebarwidth");
        ((Action)(() => UiDesignTokens.CssVar(""))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(typeof(StandardLayouts))]
    [InlineData(typeof(UiSlots))]
    [InlineData(typeof(UiDesignTokens))]
    public void Every_declared_name_constant_is_listed_in_All_exactly_once(Type holder)
    {
        var constants = holder.GetFields()
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        var all = (IReadOnlyList<string>)holder.GetField("All")!.GetValue(null)!;

        all.Should().BeEquivalentTo(constants);
        all.Should().OnlyHaveUniqueItems();
    }
}
