using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using Modulus.UI.Theming;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>Renders the real layouts through a TestServer host and asserts on the emitted HTML.</summary>
[Trait("Category", "Unit")]
public sealed partial class TablerLayoutRenderTests
{
    private static readonly string[] AllSlots = [.. UiSlots.All];

    [GeneratedRegex(@"<script(?![^>]*\ssrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyle();

    private static Task<ThemeHost> Host(
        IDictionary<string, string?>? config = null,
        Action<IServiceCollection>? services = null)
        => ThemeHost.StartAsync(config, s =>
        {
            s.AddMenuContributor<SampleMenu>();
            services?.Invoke(s);
        });

    [Fact]
    public async Task Application_layout_renders_the_shell_and_the_page_body()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Application");

        html.TrimStart().Should().StartWith("<!DOCTYPE html>", "nothing may render before the doctype");
        html.Should().Contain("probe content");
        html.Should().Contain("class=\"navbar navbar-vertical navbar-expand-lg m-sidebar\"");
        html.Should().Contain("id=\"m-main\"");
        html.Should().MatchRegex("<title>Probe (·|&#xB7;) Modulus</title>");
        html.Should().Contain("data-bs-theme=\"light\"");
        html.Should().Contain("hx-boost=\"true\"");
    }

    [Fact]
    public async Task Application_layout_renders_the_menu_with_groups_and_active_state()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/catalog/products");

        html.Should().Contain("Home");
        html.Should().Contain("Catalog");
        html.Should().Contain("href=\"/catalog/products\"");
        // The group holding the current page opens, and the matching child is marked active.
        html.Should().Contain("dropdown-menu show");
        html.Should().MatchRegex("dropdown-item active\" href=\"/catalog/products\"");
        // The nav icon partial (UI.Core) resolves from the theme via its absolute path.
        html.Should().Contain("nav-link-icon");
    }

    [Fact]
    public async Task Menu_entries_the_user_lacks_permission_for_never_reach_the_markup()
    {
        await using var denied = await Host();
        (await denied.GetStringAsync("/probe/Application")).Should().NotContain("Secret Reports");

        await using var granted = await Host(services: s =>
        {
            var user = Substitute.For<ICurrentUser>();
            user.HasPermission(Arg.Any<string>()).Returns(true);
            s.AddScoped(_ => user);
        });
        (await granted.GetStringAsync("/probe/Application")).Should().Contain("Secret Reports");
    }

    [Fact]
    public async Task Scripts_load_in_dependency_order_with_page_scripts_before_alpine()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Application");

        var order = new[]
            {
                "htmx.min.js", "idiomorph-ext.min.js", "tabler.min.js", "alpine-components.js",
                "js/modulus.js", "probe-page.js", "alpine.csp.min.js",
            }
            .Select(name => html.IndexOf(name, StringComparison.Ordinal))
            .ToArray();
        order.Should().OnlyContain(i => i >= 0);
        order.Should().BeInAscendingOrder();
        html.Should().MatchRegex("<script[^>]*src=\"/_content/[^\"]*tabler/js/modulus\\.js\"[^>]*defer");
    }

    [Fact]
    public async Task Stylesheets_load_tabler_before_the_token_layer()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Application");

        var tabler = html.IndexOf("tabler/vendor/tabler.min.css", StringComparison.Ordinal);
        var tokens = html.IndexOf("tabler/css/modulus.css", StringComparison.Ordinal);
        tabler.Should().BeGreaterThan(0);
        tokens.Should().BeGreaterThan(tabler);
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Account")]
    [InlineData("Empty")]
    [InlineData("Public")]
    public async Task Every_layout_is_csp_clean_and_ships_the_runtime_hosts(string layout)
    {
        await using var host = await Host();

        var html = await host.GetStringAsync($"/probe/{layout}");

        InlineScript().IsMatch(html).Should().BeFalse("inline <script> blocks violate script-src 'self'");
        InlineStyle().IsMatch(html).Should().BeFalse("inline style attributes violate a strict style-src");
        html.Should().Contain("id=\"modulus-toasts\"").And.Contain("aria-live=\"polite\"");
        html.Should().Contain("id=\"m-modal-container\"");
        html.Should().Contain("id=\"modulus-confirm\"");
        html.Should().Contain("name=\"__RequestVerificationToken\"");
        html.Should().Contain("probe content");
    }

    [Fact]
    public async Task Every_standard_slot_is_rendered_by_the_application_layout()
    {
        await using var host = await Host(services: s =>
        {
            foreach (var slot in AllSlots)
            {
                s.AddSingleton<ISlotContributor>(new MarkerSlot(slot));
            }
        });

        var html = await host.GetStringAsync("/probe/Application?as=alice");

        // Dashboard is a page-level slot (rendered by dashboard views), not shell chrome.
        foreach (var slot in AllSlots.Where(s => s != UiSlots.Dashboard))
        {
            html.Should().Contain($"data-slot=\"{slot}\"", $"the Application layout must render the '{slot}' slot");
        }
    }

    [Fact]
    public async Task Slot_contributions_render_in_their_regions()
    {
        await using var host = await Host(services: s =>
        {
            s.AddSingleton<ISlotContributor>(new MarkerSlot(UiSlots.Head));
            s.AddSingleton<ISlotContributor>(new MarkerSlot(UiSlots.PageBeforeContent));
            s.AddSingleton<ISlotContributor>(new MarkerSlot(UiSlots.PageAfterContent));
        });

        var html = await host.GetStringAsync("/probe/Application");

        var head = html.IndexOf($"data-slot=\"{UiSlots.Head}\"", StringComparison.Ordinal);
        var before = html.IndexOf($"data-slot=\"{UiSlots.PageBeforeContent}\"", StringComparison.Ordinal);
        var body = html.IndexOf("probe content", StringComparison.Ordinal);
        var after = html.IndexOf($"data-slot=\"{UiSlots.PageAfterContent}\"", StringComparison.Ordinal);

        head.Should().BeLessThan(html.IndexOf("</head>", StringComparison.Ordinal));
        before.Should().BeInRange(html.IndexOf("</head>", StringComparison.Ordinal), body);
        after.Should().BeGreaterThan(body);
    }

    [Fact]
    public async Task Slots_gated_by_permission_stay_hidden_from_users_without_it()
    {
        await using var host = await Host(services: s =>
            s.AddSingleton<ISlotContributor>(new MarkerSlot(UiSlots.TopbarEnd, permission: "reports:view")));

        (await host.GetStringAsync("/probe/Application")).Should().NotContain("data-slot=\"Topbar.End\"");
    }

    [Fact]
    public async Task Branding_and_feature_options_drive_the_shell()
    {
        await using var host = await Host(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Branding:AppName"] = "Acme ERP",
            ["Modulus:Ui:Branding:LogoDarkUrl"] = "/img/logo-dark.svg",
            ["Modulus:Ui:Branding:FaviconUrl"] = "/favicon.ico",
            ["Modulus:Ui:Branding:FooterText"] = "© Acme",
            ["Modulus:Ui:Features:Boost"] = "false",
            ["Modulus:Ui:Features:GlobalSearch"] = "true",
        });

        var html = await host.GetStringAsync("/probe/Application");

        html.Should().Contain("Acme ERP");
        html.Should().Contain("src=\"/img/logo-dark.svg\"");
        html.Should().Contain("rel=\"icon\" href=\"/favicon.ico\"");
        html.Should().Contain("m-footer").And.Contain("Acme");
        html.Should().Contain("hx-boost=\"false\"");
        html.Should().Contain("role=\"search\"");
    }

    [Fact]
    public async Task Footer_and_search_are_omitted_when_unconfigured()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Application");

        html.Should().NotContain("m-footer");
        html.Should().NotContain("role=\"search\"");
        html.Should().NotContain("data-m-color-mode-toggle");
    }

    [Fact]
    public async Task Server_supplied_text_is_html_encoded()
    {
        await using var host = await Host(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Branding:AppName"] = "<script>alert(1)</script>",
        });

        var html = await host.GetStringAsync("/probe/Application");

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task Signed_in_users_get_a_user_menu_anonymous_visitors_do_not()
    {
        await using var host = await Host(services: s =>
            s.AddSingleton<ISlotContributor>(new MarkerSlot(UiSlots.UserMenu)));

        var anonymous = await host.GetStringAsync("/probe/Application");
        var signedIn = await host.GetStringAsync("/probe/Application?as=alice");

        anonymous.Should().NotContain("Open user menu");
        signedIn.Should().Contain("Open user menu").And.Contain("alice").And.Contain("data-slot=\"UserMenu\"");
    }

    [Fact]
    public async Task Color_mode_follows_config_and_the_user_cookie_when_switching_is_allowed()
    {
        await using var host = await Host(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Theme:ColorMode"] = "light",
            ["Modulus:Ui:Theme:AllowUserThemeSwitch"] = "true",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe/Application");
        request.Headers.Add("Cookie", "modulus-color-mode=dark");
        var html = await (await host.Client.SendAsync(request)).Content.ReadAsStringAsync();

        html.Should().Contain("data-bs-theme=\"dark\"");
        html.Should().Contain("data-m-color-mode=\"dark\"");
        html.Should().Contain("data-m-color-mode-toggle");
    }

    [Fact]
    public async Task System_color_mode_is_handed_to_the_client()
    {
        await using var host = await Host(new Dictionary<string, string?> { ["Modulus:Ui:Theme:ColorMode"] = "system" });

        var html = await host.GetStringAsync("/probe/Application");

        html.Should().Contain("data-m-color-mode=\"system\"");
        html.Should().Contain("data-bs-theme=\"light\"");
    }

    [Fact]
    public async Task Account_layout_is_a_centered_card_without_navigation_chrome()
    {
        await using var host = await Host(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Branding:AppName"] = "Acme",
            ["Modulus:Ui:Branding:FooterText"] = "© Acme",
        });

        var html = await host.GetStringAsync("/probe/Account");

        html.Should().Contain("page page-center").And.Contain("container-tight");
        html.Should().Contain("Acme").And.Contain("m-layout-account");
        html.Should().NotContain("navbar-vertical").And.NotContain("m-topbar");
    }

    [Fact]
    public async Task Empty_layout_is_bare()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Empty");

        html.Should().Contain("m-layout-empty").And.Contain("probe content");
        html.Should().NotContain("navbar").And.NotContain("class=\"page\"").And.NotContain("m-footer");
        html.Should().Contain("modulus.js", "the runtime still loads on bare pages");
    }

    [Fact]
    public async Task Public_layout_has_a_topbar_and_footer_but_no_sidebar_or_menu()
    {
        await using var host = await Host(new Dictionary<string, string?>
        {
            ["Modulus:Ui:Branding:AppName"] = "Acme",
            ["Modulus:Ui:Branding:FooterText"] = "© Acme",
        });

        var html = await host.GetStringAsync("/probe/Public");

        html.Should().Contain("m-layout-public").And.Contain("m-topbar").And.Contain("m-footer");
        html.Should().NotContain("navbar-vertical");
        html.Should().NotContain("Catalog", "menu entries belong to the Application shell");
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Account")]
    [InlineData("Public")]
    public async Task Morph_extension_is_enabled_on_the_body_only_when_the_feature_is_on(string layout)
    {
        await using var off = await Host();
        await using var on = await Host(new Dictionary<string, string?> { ["Modulus:Ui:Features:Morph"] = "true" });

        (await off.GetStringAsync($"/probe/{layout}")).Should().NotContain("hx-ext");
        (await on.GetStringAsync($"/probe/{layout}")).Should().Contain("hx-ext=\"morph\"");
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Public")]
    public async Task Main_content_region_is_a_main_landmark_with_aria_live(string layout)
    {
        await using var host = await Host();

        var html = await host.GetStringAsync($"/probe/{layout}");

        html.Should().MatchRegex("<main class=\"container-xl m-page-container\" id=\"m-main\" aria-live=\"polite\"");
        html.Should().Contain("</main>");
        html.Should().NotContain("<div class=\"container-xl m-page-container\" id=\"m-main\">");
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Public")]
    [InlineData("Account")]
    public async Task First_focusable_element_in_body_is_a_skip_link_to_main_content(string layout)
    {
        await using var host = await Host();

        var html = await host.GetStringAsync($"/probe/{layout}");

        html.Should().MatchRegex(
            "<body[^>]*>\\s*<a class=\"visually-hidden-focusable\" href=\"#m-main\">Skip to main content</a>");
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Account")]
    [InlineData("Empty")]
    [InlineData("Public")]
    public async Task Html_lang_reflects_the_request_culture(string layout)
    {
        await using var host = await ThemeHost.StartAsync(pipeline: app => app.Use((http, next) =>
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");
            return next(http);
        }));

        var html = await host.GetStringAsync($"/probe/{layout}");

        html.Should().Contain("<html lang=\"fr-FR\"");
        html.Should().NotContain("<html lang=\"en\"");
    }

    [Fact]
    public async Task Shared_alert_partial_uses_a_named_csp_safe_alpine_component()
    {
        await using var host = await Host();

        var html = await host.GetStringAsync("/probe/Alert");

        html.Should().Contain("x-data=\"mDismissibleAlert\"").And.Contain("x-on:click=\"dismiss\"");
        html.Should().NotMatchRegex("x-(data|init)=\"\\{", "the Alpine CSP build cannot evaluate inline expressions");
        html.IndexOf("alpine-components.js", StringComparison.Ordinal)
            .Should().BeInRange(0, html.IndexOf("alpine.csp.min.js", StringComparison.Ordinal),
                "components must register before Alpine starts");
    }

    private sealed class SampleMenu : IMenuContributor
    {
        public ValueTask ConfigureAsync(MenuConfigurationContext context, CancellationToken cancellationToken = default)
        {
            context.Main
                .AddItem("Home", "Home", "/", icon: "building", order: 1)
                .AddGroup("Catalog", "Catalog", icon: "folder", order: 10)
                .AddItem("Catalog.Products", "Products", "/catalog/products", groupId: "Catalog")
                .AddItem("Reports.Secret", "Secret Reports", "/reports", requiredPermission: "reports:view", order: 50);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MarkerSlot(string slot, string? permission = null) : ISlotContributor
    {
        public string Slot => slot;

        public string? RequiredPermission => permission;

        public ValueTask<IHtmlContent?> RenderAsync(SlotContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IHtmlContent?>(new HtmlString($"<i data-slot=\"{slot}\"></i>"));
    }
}
