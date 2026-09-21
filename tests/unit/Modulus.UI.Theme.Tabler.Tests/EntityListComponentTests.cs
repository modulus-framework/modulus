using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// Contributed columns and row actions rendered through the real Razor pipeline (<c>Pages/Probe/EntityListPage</c>):
/// headers from <c>m-datatable entity</c>, cells from <c>m-entity-cells</c>, buttons from <c>m-entity-actions</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EntityListComponentTests
{
    private const string Page = "/Probe/EntityListPage";

    public sealed class StockProvider : IEntityColumnValueProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, string?>> LoadAsync(IReadOnlyCollection<string> rowIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            // Row 2 has no stock record; the value is markup-looking text that must come out encoded.
            return Task.FromResult<IReadOnlyDictionary<string, string?>>(
                rowIds.Where(id => id == "1").ToDictionary(id => id, _ => (string?)"<b>12</b>"));
        }
    }

    private static Task<ThemeHost> Start(StockProvider provider, params string[] permissions)
        => ThemeHost.StartAsync(services: s =>
        {
            var user = Substitute.For<ICurrentUser>();
            user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
            s.AddSingleton(user);
            s.AddSingleton(provider);

            // What an "Inventory" module would do from its ConfigureServices.
            s.ConfigureEntityUi("Catalog.Product", e =>
            {
                e.Columns.Add(new EntityColumn("Stock", "Stock", typeof(StockProvider), order: 45, cssClass: "text-end"));
                e.Actions.Add(new EntityAction("Inventory.Adjust", "Adjust stock", hxGet: "/Inventory/Adjust?productId={id}", order: 20, requiredPermission: "inventory.adjust"));
                e.Actions.Add(new EntityAction("Inventory.Archive", "Archive", hxPost: "/Inventory/Archive/{id}", target: EntityActionTarget.Row, order: 30, cssClass: "btn-outline-danger", requiredPermission: "inventory.adjust"));
                e.Actions.Add(new EntityAction("Inventory.History", "History", url: "/Inventory/History/{id}", order: 10));
            });
        });

    [Fact]
    public async Task Contributed_column_headers_sit_where_the_marker_is_and_cells_line_up_with_them()
    {
        var provider = new StockProvider();
        await using var host = await Start(provider);

        var html = await host.GetStringAsync(Page);

        // Headers: Name, then the contributed Stock (at the <m-entity-columns /> marker), then the page's actions column.
        html.Should().MatchRegex(@"<th>Name</th>\s*<th class=""text-end"">Stock</th>\s*<th class=""text-end""></th>");
        // Cells for row 1: name, the stock value (encoded), then the actions cell.
        html.Should().MatchRegex(@"<td>Widget</td>\s*<td class=""text-end"">\s*&lt;b&gt;12&lt;/b&gt;\s*</td>");
        // Row 2 has no value: a dash keeps the table aligned.
        html.Should().MatchRegex(@"<td>Gadget</td>\s*<td class=""text-end"">\s*<span class=""text-secondary"">&mdash;</span>");
        provider.Calls.Should().Be(1, "the whole page of rows is loaded in one call");
    }

    [Fact]
    public async Task Cells_render_a_dash_when_the_page_did_not_load_values()
    {
        await using var host = await Start(new StockProvider());

        var html = await host.GetStringAsync($"{Page}?skipLoad=true");

        html.Should().Contain("<th class=\"text-end\">Stock</th>");
        html.Should().NotContain("&lt;b&gt;12");
        System.Text.RegularExpressions.Regex.Matches(html, "<span class=\"text-secondary\">&mdash;</span>").Should().HaveCount(2, "each row shows a dash");
    }

    [Fact]
    public async Task The_empty_state_row_spans_the_contributed_columns_too()
    {
        await using var host = await Start(new StockProvider());

        // A source keeps the table shell even when empty, which is where the colspan row lives.
        var html = await host.GetStringAsync($"{Page}?empty=true&live=true");

        // Name + Stock + actions column = 3, not the page's own 2.
        html.Should().Contain("<td colspan=\"3\"").And.Contain("No products");
    }

    [Fact]
    public async Task Row_actions_render_by_permission_with_the_row_id_expanded_and_their_target()
    {
        await using var host = await Start(new StockProvider(), "inventory.adjust");

        var html = await host.GetStringAsync(Page);

        // Order 10 < 20 < 30; a link, a modal GET and a row-replacing POST.
        html.Should().Contain("<a class=\"btn btn-sm btn-outline-secondary\" href=\"/Inventory/History/1\">History</a>");
        html.Should().Contain("hx-get=\"/Inventory/Adjust?productId=2\"").And.Contain("hx-target=\"#m-modal-container\"");
        html.Should().Contain("hx-post=\"/Inventory/Archive/1\"").And.Contain("hx-target=\"closest tr\"").And.Contain("hx-swap=\"outerHTML\"");
        html.Should().Contain("btn btn-sm btn-outline-danger");
        html.IndexOf("/Inventory/History/1", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("/Inventory/Adjust?productId=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Row_actions_the_user_lacks_permission_for_are_not_rendered()
    {
        await using var host = await Start(new StockProvider());

        var html = await host.GetStringAsync(Page);

        html.Should().Contain("/Inventory/History/1");
        html.Should().NotContain("Inventory/Adjust").And.NotContain("Inventory/Archive");
    }
}
