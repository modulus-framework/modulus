using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// <c>m-fields</c> rendered through the real Razor pipeline (<c>Pages/Probe/EntityFieldsPage</c>): fields another
/// module contributed to <c>Catalog.Product</c> show up in the form, post as a bag, and validate on the server.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EntityFieldsComponentTests
{
    private const string Page = "/Probe/EntityFieldsPage";

    private static Task<ThemeHost> Start(params string[] permissions)
        => ThemeHost.StartAsync(services: s =>
        {
            var user = Substitute.For<ICurrentUser>();
            user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
            s.AddSingleton(user);

            // What an "Inventory" module would do from its ConfigureServices.
            s.ConfigureEntityUi("Catalog.Product", e =>
            {
                e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", tab: "Inventory", order: 20,
                    validators: [new RangeAttribute(0, 1000)], hint: "Restock below this"));
                e.Fields.Add(new EntityField("Active", typeof(bool), "Stocked", tab: "Inventory", order: 30));
                e.Fields.Add(new EntityField("Supplier", typeof(string), "Supplier", order: 5, validators: [new RequiredAttribute()]));
                e.Fields.Add(new EntityField("Bin", typeof(string), "Bin", tab: "Storage", order: 40, requiredPermission: "inventory.manage"));
            });
        });

    private static FormUrlEncodedContent Post(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    [Fact]
    public async Task Contributed_fields_render_in_order_with_bag_names_labels_types_and_current_values()
    {
        await using var host = await Start();

        var html = await host.GetStringAsync(Page);

        html.Should().Contain("data-entity-fields=\"Catalog.Product\"");
        html.Should().Contain("name=\"Input.Extra[Supplier]\"").And.Contain("id=\"Input_Extra_Supplier_\"");
        // [Required] validator -> the required marker; order 5 < 20 < 30.
        html.Should().Contain("<label class=\"form-label required\" for=\"Input_Extra_Supplier_\">Supplier</label>");
        html.IndexOf("Input.Extra[Supplier]", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("Input.Extra[ReorderLevel]", StringComparison.Ordinal));
        html.IndexOf("Input.Extra[ReorderLevel]", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("Input.Extra[Active]", StringComparison.Ordinal));
        // decimal -> number with step any, prefilled from the bag; hint carried through.
        html.Should().MatchRegex("<input type=\"number\"[^>]*name=\"Input\\.Extra\\[ReorderLevel\\]\"[^>]*value=\"10\"[^>]*step=\"any\"");
        html.Should().Contain("Restock below this");
        // bool -> checkbox, ticked from "true".
        html.Should().MatchRegex("<input type=\"checkbox\"[^>]*name=\"Input\\.Extra\\[Active\\]\"[^>]*checked");
    }

    [Fact]
    public async Task A_field_needing_a_permission_the_user_lacks_is_not_rendered()
    {
        await using var host = await Start();

        var html = await host.GetStringAsync(Page);

        html.Should().NotContain("Input.Extra[Bin]");
    }

    [Fact]
    public async Task A_field_needing_a_permission_the_user_has_is_rendered()
    {
        await using var host = await Start("inventory.manage");

        var html = await host.GetStringAsync(Page);

        html.Should().Contain("name=\"Input.Extra[Bin]\"");
    }

    [Fact]
    public async Task The_tab_attribute_renders_only_that_tabs_fields()
    {
        await using var host = await Start("inventory.manage");

        var html = await host.GetStringAsync($"{Page}?tab=inventory");

        html.Should().Contain("Input.Extra[ReorderLevel]").And.Contain("Input.Extra[Active]");
        html.Should().NotContain("Input.Extra[Supplier]").And.NotContain("Input.Extra[Bin]");
    }

    [Fact]
    public async Task An_entity_nobody_contributed_to_renders_no_wrapper()
    {
        await using var host = await Start();

        var html = await host.GetStringAsync($"{Page}?entity=Sales.Order");

        html.Should().NotContain("data-entity-fields").And.NotContain("Input.Extra[");
    }

    [Fact]
    public async Task A_valid_post_reads_back_typed_values_of_the_visible_fields_only()
    {
        await using var host = await Start();

        using var response = await host.Client.PostAsync(Page, Post(
            ("Input.Name", "Widget"),
            ("Input.Extra[Supplier]", "Acme"),
            ("Input.Extra[ReorderLevel]", "25.5"),
            ("Input.Extra[Active]", "true"),
            ("Input.Extra[Active]", "false"), // the checkbox's hidden companion
            ("Input.Extra[Bin]", "A-7")));      // the user has no permission for Bin: never offered, so ignored

        (await response.Content.ReadAsStringAsync()).Should().Be("Active=True|ReorderLevel=25.5|Supplier=Acme");
    }

    [Fact]
    public async Task A_rejected_post_keeps_the_typed_values_and_shows_each_error_on_its_field()
    {
        await using var host = await Start();

        using var response = await host.Client.PostAsync(Page, Post(
            ("Input.Name", "Widget"),
            ("Input.Extra[ReorderLevel]", "5000")));   // out of range, and the required Supplier is missing
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("must be between 0 and 1000");
        html.Should().Contain("The Supplier field is required.");
        html.Should().MatchRegex("name=\"Input\\.Extra\\[ReorderLevel\\]\"[^>]*value=\"5000\"");
    }

    [Fact]
    public async Task Text_that_is_not_a_number_is_reported_and_kept()
    {
        await using var host = await Start();

        using var response = await host.Client.PostAsync(Page, Post(
            ("Input.Name", "Widget"),
            ("Input.Extra[Supplier]", "Acme"),
            ("Input.Extra[ReorderLevel]", "abc")));
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("is not valid for Reorder level");
        html.Should().MatchRegex("name=\"Input\\.Extra\\[ReorderLevel\\]\"[^>]*value=\"abc\"");
    }
}
