using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// Renders the component tag helpers (m-card, m-datatable, m-form, ...) through the real Razor
/// pipeline: tag helper -> view model -> overridable partial resolved by <see cref="IModulusViewResolver"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TablerComponentRenderTests
{
    private sealed class ProductCrumbs : IBreadcrumbContributor
    {
        public ValueTask ConfigureAsync(BreadcrumbContext context, CancellationToken cancellationToken = default)
        {
            if (context.PageId == "Catalog.Products.Index")
            {
                context.Add("Home", "/").Add("Catalog", "/catalog").Add("Products");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProductToolbar : IToolbarContributor
    {
        public ValueTask ConfigureAsync(ToolbarContext context, CancellationToken cancellationToken = default)
        {
            if (context.PageId == "Catalog.Products.Index")
            {
                context.Add(new ToolbarItem("New", "New product", Url: "/catalog/new", Icon: "folder", Order: 1));
                context.Add(new ToolbarItem("Import", "Import", HxGet: "/catalog/import", Order: 2));
                context.Add(new ToolbarItem("Purge", "Purge", HxPost: "/catalog/purge", Order: 3, Target: "#result", CssClass: "btn-danger"));
                context.Add(new ToolbarItem("Secret", "Audit", Url: "/audit", RequiredPermission: "audit:view", Order: 4));
            }

            return ValueTask.CompletedTask;
        }
    }

    private static Task<ThemeHost> Host(
        IDictionary<string, string?>? config = null,
        bool grantAll = false)
        => ThemeHost.StartAsync(config, s =>
        {
            s.AddBreadcrumbContributor<ProductCrumbs>();
            s.AddToolbarContributor<ProductToolbar>();
            if (grantAll)
            {
                var user = Substitute.For<ICurrentUser>();
                user.HasPermission(Arg.Any<string>()).Returns(true);
                s.AddScoped(_ => user);
            }
        });

    private static async Task<string> Html(ThemeHost host, string url) => await host.GetStringAsync(url);

    // ---- m-card ----------------------------------------------------------------

    [Fact]
    public async Task Card_renders_a_header_and_pads_the_body_by_default()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Card");

        html.Should().Contain("<h3 class=\"card-title\">Details</h3>");
        // The first card is unpadded (table inside); the second, untitled card pads its body.
        html.Should().MatchRegex("<div class=\"card\">\\s*<div class=\"card-header\">[\\s\\S]*?<table id=\"inner\">");
        html.Should().NotMatchRegex("card-body\">\\s*<table id=\"inner\"");
        html.Should().MatchRegex("<div class=\"card-body\">\\s*<p id=\"body\">plain body</p>");
        Regex.Matches(html, "card-header").Should().HaveCount(1);
    }

    // ---- m-datatable / m-pagination -----------------------------------------------

    [Fact]
    public async Task Datatable_renders_headers_rows_and_the_card_shell()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTable?PageNumber=2");

        html.Should().Contain("id=\"users\"");
        html.Should().Contain("table table-vcenter card-table");
        html.Should().Contain("<th>Name</th>").And.Contain("<th class=\"text-end\"></th>");
        html.Should().Contain("<td>Alice</td>").And.Contain("<td>Bob</td>");
        html.IndexOf("<thead>", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("<td>Alice</td>", StringComparison.Ordinal));
        // Column elements are consumed, not echoed into the output.
        html.Should().NotContain("<m-column");
    }

    [Fact]
    public async Task Datatable_pager_keeps_filters_drops_the_handler_and_layers_route_values()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTable?PageNumber=2&Action=login&handler=List");

        html.Should().Contain("Page 2");
        html.Should().Contain("href=\"/probe/DataTable?Action=login&amp;Extra=1&amp;PageNumber=1\"");
        html.Should().Contain("href=\"/probe/DataTable?Action=login&amp;Extra=1&amp;PageNumber=3\"");
        html.Should().NotContain("handler=");
        html.Should().Contain("rel=\"prev\"").And.Contain("rel=\"next\"");
    }

    [Fact]
    public async Task Datatable_without_a_page_has_no_pager()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTableUnpaged");

        html.Should().Contain("<td>Only</td>");
        html.Should().NotContain("card-footer");
    }

    [Fact]
    public async Task Datatable_empty_state_replaces_the_table()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTableEmpty");

        html.Should().Contain("alert alert-info").And.Contain("No users yet");
        html.Should().NotContain("<table").And.NotContain("<thead>");
    }

    [Fact]
    public async Task Pagination_standalone_only_links_the_directions_that_exist()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Pagination?PageNumber=3&Name=a%26b");

        html.Should().Contain("Seite 3");
        html.Should().Contain("rel=\"prev\"").And.NotContain("rel=\"next\"");
        // Query values are re-encoded; route- values are appended.
        html.Should().Contain("Name=a%26b").And.Contain("UnreadOnly=true").And.Contain("PageNumber=2");
    }

    [Fact]
    public async Task Datatable_with_a_source_requeries_its_rows_on_entity_changed_events()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTableLive");
        var live = html[html.IndexOf("id=\"live\"", StringComparison.Ordinal)..html.IndexOf("id=\"lazy\"", StringComparison.Ordinal)];

        live.Should().Contain("<tbody hx-get=\"/products?handler=Rows\"");
        live.Should().Contain("hx-trigger=\"product:changed from:body, order:changed from:body\"").And.Contain("hx-swap=\"innerHTML\"");
        live.Should().Contain("<td>Alice</td>", "the first render still carries the page's own rows");
    }

    [Fact]
    public async Task Datatable_source_without_refresh_events_loads_once_and_keeps_its_shell_when_empty()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTableLive");
        var lazy = html[html.IndexOf("id=\"lazy\"", StringComparison.Ordinal)..html.IndexOf("id=\"static\"", StringComparison.Ordinal)];

        lazy.Should().Contain("hx-get=\"/lazy\"").And.Contain("hx-trigger=\"load\"");
        // An empty grid with a source must still render its table, or a later refresh has nowhere to land.
        lazy.Should().Contain("<table").And.NotContain("alert-info");
        lazy.Should().MatchRegex("<td colspan=\"2\" class=\"text-secondary text-center\">Nothing yet</td>");
    }

    [Fact]
    public async Task Datatable_without_a_source_stays_a_plain_table()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/DataTableLive");
        var plain = html[html.IndexOf("id=\"static\"", StringComparison.Ordinal)..];

        plain.Should().NotContain("hx-get").And.NotContain("hx-trigger");
        plain.Should().Contain("<td>Zed</td>");
    }

    [Fact]
    public async Task Datatable_rejects_refresh_events_that_are_not_entity_changed_events_or_lack_a_source()
    {
        static TagHelperOutput Output() => new("m-datatable", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        static TagHelperContext Context()
        {
            var helper = new DataTableTagHelper();
            var context = new TagHelperContext([], new Dictionary<object, object>(), "id");
            helper.Init(context);
            return context;
        }

        var badEvent = () => new DataTableTagHelper { Source = "/x", RefreshOn = "product:updated" }.ProcessAsync(Context(), Output());
        var noSource = () => new DataTableTagHelper { RefreshOn = "product:changed" }.ProcessAsync(Context(), Output());

        await badEvent.Should().ThrowAsync<InvalidOperationException>().WithMessage("*product:changed*");
        await noSource.Should().ThrowAsync<InvalidOperationException>().WithMessage("*needs a source*");
    }

    // ---- m-form ------------------------------------------------------------------

    [Fact]
    public async Task Form_posts_to_the_page_handler_and_becomes_an_htmx_form_when_targeted()
    {
        await using var host = await Host();

        var html = await Html(host, "/Probe/FormPage");
        var handlerForm = html[html.IndexOf("id=\"handler-form\"", StringComparison.Ordinal)..html.IndexOf("id=\"modal-form\"", StringComparison.Ordinal)];

        handlerForm.Should().Contain("method=\"post\"");
        handlerForm.Should().Contain("action=\"/Probe/FormPage?id=7&amp;handler=Save\"");
        handlerForm.Should().Contain("hx-post=\"/Probe/FormPage?id=7&amp;handler=Save\"");
        handlerForm.Should().Contain("hx-target=\"#form-region\"").And.Contain("hx-swap=\"innerHTML\"");
        handlerForm.Should().Contain("name=\"x\"");
    }

    [Fact]
    public async Task Form_without_a_target_is_a_plain_post_and_modal_targets_the_shared_container()
    {
        await using var host = await Host();

        var html = await Html(host, "/Probe/FormPage");
        var modalForm = html[html.IndexOf("id=\"modal-form\"", StringComparison.Ordinal)..html.IndexOf("id=\"plain-form\"", StringComparison.Ordinal)];
        var plainForm = html[html.IndexOf("id=\"plain-form\"", StringComparison.Ordinal)..html.IndexOf("id=\"custom-form\"", StringComparison.Ordinal)];

        modalForm.Should().Contain("hx-target=\"#m-modal-container\"").And.Contain("hx-swap=\"outerHTML\"").And.Contain("class=\"p-2\"");
        plainForm.Should().Contain("action=\"/Probe/FormPage?handler=Save\"");
        plainForm.Should().NotContain("hx-");
    }

    [Fact]
    public async Task Form_action_can_be_overridden_explicitly()
    {
        await using var host = await Host();

        var html = await Html(host, "/Probe/FormPage");
        var custom = html[html.IndexOf("id=\"custom-form\"", StringComparison.Ordinal)..];

        custom.Should().Contain("action=\"/custom/post\"").And.Contain("hx-post=\"/custom/post\"").And.Contain("hx-target=\"#t\"");
    }

    [Fact]
    public async Task Every_post_form_carries_exactly_one_antiforgery_token()
    {
        await using var host = await Host();

        var html = await Html(host, "/Probe/FormPage");

        foreach (var section in new[] { "handler-form", "modal-form", "plain-form", "custom-form" })
        {
            var start = html.IndexOf($"id=\"{section}\"", StringComparison.Ordinal);
            var end = html.IndexOf("</form>", start, StringComparison.Ordinal);
            Regex.Matches(html[start..end], "__RequestVerificationToken").Should().HaveCount(1, $"'{section}' must not duplicate the token");
        }
    }

    [Fact]
    public async Task Form_shows_the_validation_summary_only_for_an_invalid_model_and_when_enabled()
    {
        await using var host = await Host();

        var valid = await Html(host, "/Probe/FormPage");
        var invalid = await Html(host, "/Probe/FormPage?invalid=1");

        valid.Should().NotContain("Something failed");
        // Three forms show the summary, the fourth opted out with summary="false".
        Regex.Matches(invalid, "Something failed").Should().HaveCount(3);
        invalid[invalid.IndexOf("id=\"custom-form\"", StringComparison.Ordinal)..].Should().NotContain("Something failed");
    }

    // ---- m-modal -----------------------------------------------------------------

    [Fact]
    public async Task Modal_renders_header_body_and_footer()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Modal");

        html.Should().Contain("<h5 class=\"modal-title\">New user</h5>");
        html.Should().Contain("data-bs-dismiss=\"modal\"");
        html.Should().MatchRegex("<div class=\"modal-body\">\\s*<p id=\"modal-body\">form here</p>");
        html.Should().MatchRegex("<div class=\"modal-footer\">\\s*<button id=\"save\">Save</button>");
        html.Should().NotContain("<m-modal");
    }

    [Fact]
    public async Task Modal_without_a_footer_or_close_button_omits_them()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Modal");
        // Stop at the end of the page body: the layout's own confirm modal has a footer.
        var start = html.IndexOf("Read only", StringComparison.Ordinal);
        var second = html[start..html.IndexOf("</main>", start, StringComparison.Ordinal)];

        second.Should().Contain("no footer");
        second.Should().NotContain("modal-footer").And.NotContain("btn-close");
    }

    // ---- m-tabs ------------------------------------------------------------------

    [Fact]
    public async Task Tabs_select_the_first_tab_and_namespace_pane_ids()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Tabs");

        Regex.Matches(html, "nav-link active").Should().HaveCount(1);
        html.Should().Contain("href=\"#probe-tabs-general\"").And.Contain("id=\"probe-tabs-general\"");
        html.Should().MatchRegex("tab-pane active show\" id=\"probe-tabs-general\"");
        html.Should().Contain("data-bs-toggle=\"tab\"").And.Contain("role=\"tablist\"");
        html.Should().Contain("aria-selected=\"true\"").And.Contain("aria-selected=\"false\"");
    }

    [Fact]
    public async Task Tabs_cross_reference_each_tab_and_its_pane_for_assistive_tech()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Tabs");

        // Each tab <a> gets a stable id its pane points back at, and points at its pane in turn.
        html.Should().Contain("id=\"tab-probe-tabs-general\"").And.Contain("aria-controls=\"probe-tabs-general\"");
        html.Should().Contain("id=\"tab-probe-tabs-audit\"").And.Contain("aria-controls=\"probe-tabs-audit\"");
        html.Should().Contain("id=\"tab-probe-tabs-more\"").And.Contain("aria-controls=\"probe-tabs-more\"");
        html.Should().MatchRegex("role=\"tabpanel\" aria-labelledby=\"tab-probe-tabs-general\"");
        html.Should().MatchRegex("role=\"tabpanel\" aria-labelledby=\"tab-probe-tabs-audit\"");
        html.Should().MatchRegex("role=\"tabpanel\" aria-labelledby=\"tab-probe-tabs-more\"");
    }

    [Fact]
    public async Task Tabs_wire_a_csp_safe_alpine_component_for_arrow_key_navigation()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Tabs");

        html.Should().Contain("x-data=\"mTabs\"").And.Contain("x-on:keydown=\"onKeydown\"");
        html.Should().NotMatchRegex("x-(data|on:keydown)=\"\\{", "the Alpine CSP build cannot evaluate inline expressions");
    }

    [Fact]
    public async Task Tabs_lazy_load_a_source_pane_with_htmx_and_never_render_its_body()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Tabs");

        html.Should().Contain("hx-get=\"/audit/list\"").And.Contain("hx-trigger=\"intersect once\"");
        html.Should().NotContain("never rendered");
    }

    [Fact]
    public async Task Tabs_respect_an_explicitly_active_tab()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/TabsExplicit");

        Regex.Matches(html, "nav-link active").Should().HaveCount(1);
        html.Should().MatchRegex("tab-pane active show\" id=\"t2-b\"");
        html.Should().NotMatchRegex("tab-pane active show\" id=\"t2-a\"");
    }

    // ---- m-breadcrumbs -----------------------------------------------------------

    [Fact]
    public async Task Breadcrumbs_from_the_page_link_every_item_but_the_last_and_encode_titles()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/BreadcrumbsExplicit");

        html.Should().Contain("<a href=\"/\">Home</a>").And.Contain("<a href=\"/users\">Users</a>");
        html.Should().Contain("breadcrumb-item active\" aria-current=\"page\"");
        html.Should().Contain("&lt;Alice&gt;").And.NotContain("<Alice>");
        html.Should().NotContain("<a href=\"\">");
    }

    [Fact]
    public async Task Breadcrumbs_come_from_contributors_for_the_page_id()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/BreadcrumbsPageId");

        html.Should().Contain("<a href=\"/catalog\">Catalog</a>");
        html.Should().MatchRegex("breadcrumb-item active\" aria-current=\"page\">\\s*Products");
    }

    [Fact]
    public async Task Breadcrumbs_render_nothing_without_a_trail_or_when_the_feature_is_off()
    {
        await using var host = await Host();
        await using var off = await Host(new Dictionary<string, string?> { ["Modulus:Ui:Features:Breadcrumbs"] = "false" });

        (await Html(host, "/probe/BreadcrumbsNone")).Should().NotContain("breadcrumb");
        (await Html(off, "/probe/BreadcrumbsPageId")).Should().NotContain("breadcrumb");
    }

    [Fact]
    public async Task The_application_layout_shows_the_trail_above_the_page_body()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/AppCrumbs");

        html.IndexOf("breadcrumb-item", StringComparison.Ordinal).Should().BePositive();
        html.IndexOf("breadcrumb-item", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("page body", StringComparison.Ordinal));
    }

    // ---- m-toolbar / m-page-header ------------------------------------------------

    [Fact]
    public async Task Toolbar_renders_each_item_kind_with_sensible_defaults()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/Toolbar");

        html.Should().MatchRegex("<a class=\"btn btn-primary\" href=\"/catalog/new\">[\\s\\S]*?New product</a>");
        html.Should().Contain("class=\"icon me-1\"", "the item's icon renders inline");
        html.Should().Contain("<button type=\"button\" class=\"btn btn-outline-secondary\" hx-get=\"/catalog/import\" hx-target=\"#m-modal-container\" hx-swap=\"innerHTML\">Import</button>");
        html.Should().Contain("class=\"btn btn-danger\" hx-post=\"/catalog/purge\" hx-target=\"#result\"");
    }

    [Fact]
    public async Task Toolbar_hides_items_the_user_may_not_use()
    {
        await using var denied = await Host();
        await using var granted = await Host(grantAll: true);

        (await Html(denied, "/probe/Toolbar")).Should().NotContain("href=\"/audit\"");
        (await Html(granted, "/probe/Toolbar")).Should().Contain("href=\"/audit\"");
    }

    [Fact]
    public async Task Toolbar_renders_nothing_without_a_page_id_or_items()
    {
        await using var host = await Host();

        (await Html(host, "/probe/ToolbarNoPage")).Should().NotContain("btn-list");
    }

    [Fact]
    public async Task Page_header_appends_contributed_toolbar_items_after_its_own_actions()
    {
        await using var host = await Host();

        var html = await Html(host, "/probe/PageHeader");
        var first = html[..html.IndexOf("Bare", StringComparison.Ordinal)];

        first.Should().Contain("<h1 class=\"page-title\">Products</h1>");
        first.IndexOf("Own action", StringComparison.Ordinal).Should().BeLessThan(first.IndexOf("New product", StringComparison.Ordinal));
        first.Should().Contain("col-auto ms-auto");
        // A page id with no contributed items adds no actions column.
        html[html.IndexOf("Bare", StringComparison.Ordinal)..].Should().NotContain("col-auto");
    }
}
