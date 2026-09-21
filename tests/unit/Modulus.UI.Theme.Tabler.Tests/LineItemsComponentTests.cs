using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary><c>m-line-items</c> rendered through the real Razor pipeline (<c>Pages/Probe/LineItemsPage</c>).</summary>
[Trait("Category", "Unit")]
public sealed class LineItemsComponentTests
{
    private static FormUrlEncodedContent Post(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    [Fact]
    public async Task Existing_items_render_as_rows_with_indexed_names_and_ids_and_their_values()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/LineItemsPage");

        html.Should().Contain("x-data=\"mLineItems\"")
            .And.Contain("data-name-prefix=\"Input.Lines\"")
            .And.Contain("data-id-prefix=\"Input_Lines\"");
        html.Should().Contain("name=\"Input.Lines[0].Sku\"").And.Contain("value=\"A-1\"");
        html.Should().Contain("name=\"Input.Lines[1].Sku\"").And.Contain("value=\"B-2\"");
        html.Should().Contain("name=\"Input.Lines[1].Quantity\"").And.Contain("value=\"5\"");
        html.Should().Contain("id=\"Input_Lines_1__Sku\"").And.Contain("for=\"Input_Lines_1__Sku\"");
        // Label from the element's [Display] metadata, required marker from [Required].
        html.Should().Contain("<label class=\"form-label required\" for=\"Input_Lines_0__Sku\">SKU</label>");
        html.Should().Contain(">Add line</button>").And.Contain("aria-label=\"Drop\"").And.Contain("At least one line");
    }

    [Fact]
    public async Task A_blank_template_row_carries_the_index_placeholder_for_the_client_to_fill()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/LineItemsPage");

        var template = html[html.IndexOf("<template data-line-template>", StringComparison.Ordinal)..html.IndexOf("</template>", StringComparison.Ordinal)];
        template.Should().Contain("name=\"Input.Lines[__index__].Sku\"").And.Contain("name=\"Input.Lines[__index__].Quantity\"");
        template.Should().Contain("id=\"Input_Lines___index____Sku\"");
        template.Should().NotContain("A-1", "the template is a blank row, not a copy of an item");
        // Both hooks the Alpine component needs: rows and the template live under the x-data element.
        html.Should().Contain("data-line-rows").And.Contain("x-on:click=\"add\"").And.Contain("x-on:click=\"remove\"");
    }

    [Fact]
    public async Task An_empty_collection_still_offers_the_add_button_and_template()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/LineItemsPage?empty=true");

        html.Should().NotContain("name=\"Input.Lines[0]");
        html.Should().Contain("<template data-line-template>").And.Contain("x-on:click=\"add\"");
    }

    [Fact]
    public async Task Posted_rows_bind_as_the_collection()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync("/Probe/LineItemsPage", Post(
            ("Input.Lines[0].Sku", "A-1"), ("Input.Lines[0].Quantity", "2"),
            ("Input.Lines[1].Sku", "C-3"), ("Input.Lines[1].Quantity", "7")));

        (await response.Content.ReadAsStringAsync()).Should().Be("A-1x2|C-3x7");
    }

    [Fact]
    public async Task A_rejected_post_keeps_each_rows_values_and_shows_the_error_on_the_right_row()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync("/Probe/LineItemsPage", Post(
            ("Input.Lines[0].Sku", "A-1"), ("Input.Lines[0].Quantity", "2"),
            ("Input.Lines[1].Sku", string.Empty), ("Input.Lines[1].Quantity", "500")));
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("name=\"Input.Lines[0].Sku\"").And.Contain("value=\"A-1\"");
        html.Should().Contain("value=\"500\"", "the rejected quantity is echoed back");
        var firstRow = html[html.IndexOf("Input_Lines_0__Sku", StringComparison.Ordinal)..html.IndexOf("Input_Lines_1__Sku", StringComparison.Ordinal)];
        firstRow.Should().NotContain("is-invalid");
        var secondRow = html[html.IndexOf("Input_Lines_1__Sku", StringComparison.Ordinal)..html.IndexOf("<template", StringComparison.Ordinal)];
        secondRow.Should().Contain("is-invalid").And.Contain("The SKU field is required.");
        secondRow.Should().Contain("The field Quantity must be between 1 and 100.");
    }
}
