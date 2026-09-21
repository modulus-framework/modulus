using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.UI;
using Modulus.UI.Theming;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for how feature UIs pick a layout from their _ViewStart without referencing a theme.</summary>
[Trait("Category", "Unit")]
public sealed class ThemeLayoutExtensionsTests
{
    private sealed class AcmeTheme : ITheme
    {
        public string Name => "Tabler";

        public string GetLayout(string layoutName) => $"/Themes/Acme/{layoutName}.cshtml";

        public IReadOnlyList<ThemeAsset> Styles { get; } = [];

        public IReadOnlyList<ThemeAsset> Scripts { get; } = [];
    }

    private static HttpContext Context(Action<IServiceCollection>? configure = null, Action<HttpRequest>? request = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        request?.Invoke(http.Request);
        return http;
    }

    [Fact]
    public void Without_a_registered_theme_the_legacy_shell_is_used_so_older_apps_keep_rendering()
    {
        var http = Context(s => s.AddModulusUi());

        http.GetThemeLayout(StandardLayouts.Application).Should().Be(ThemeLayoutExtensions.LegacyLayout);
        http.GetThemeLayout(StandardLayouts.Account).Should().Be("_UiLayout");
    }

    [Theory]
    [InlineData(StandardLayouts.Application)]
    [InlineData(StandardLayouts.Account)]
    public void With_a_theme_the_layout_comes_from_the_active_theme(string layout)
    {
        var http = Context(s => s.AddModulusTheme<AcmeTheme>());

        http.GetThemeLayout(layout).Should().Be($"/Themes/Acme/{layout}.cshtml");
    }

    [Fact]
    public void The_default_layout_is_Application()
    {
        Context(s => s.AddModulusTheme<AcmeTheme>()).GetThemeLayout()
            .Should().Be("/Themes/Acme/Application.cshtml");
    }

    [Fact]
    public void Layout_remapping_from_ThemeOptions_is_honored()
    {
        var http = Context(s =>
        {
            s.AddModulusTheme<AcmeTheme>();
            s.Configure<ThemeOptions>(o => o.Layouts["Application"] = "Public");
        });

        http.GetThemeLayout(StandardLayouts.Application).Should().Be("/Themes/Acme/Public.cshtml");
    }

    [Fact]
    public void Htmx_fragment_requests_get_no_layout_so_swaps_never_nest_a_second_shell()
    {
        var http = Context(s => s.AddModulusTheme<AcmeTheme>(), r => r.Headers["HX-Request"] = "true");

        http.GetThemeLayout(StandardLayouts.Application).Should().BeNull();
    }

    [Fact]
    public void Fragment_detection_applies_to_the_legacy_shell_too()
    {
        var http = Context(s => s.AddModulusUi(), r => r.Headers["HX-Request"] = "true");

        http.GetThemeLayout().Should().BeNull();
    }

    [Fact]
    public void Boosted_navigations_still_render_the_full_shell()
    {
        var http = Context(
            s => s.AddModulusTheme<AcmeTheme>(),
            r =>
            {
                r.Headers["HX-Request"] = "true";
                r.Headers["HX-Boosted"] = "true";
            });

        http.GetThemeLayout().Should().Be("/Themes/Acme/Application.cshtml");
    }

    [Fact]
    public void A_blank_layout_name_is_rejected()
    {
        var http = Context(s => s.AddModulusUi());

        ((Action)(() => http.GetThemeLayout(" "))).Should().Throw<ArgumentException>();
    }
}
