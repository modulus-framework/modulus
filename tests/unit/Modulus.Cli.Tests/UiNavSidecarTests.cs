using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The nav sidecar and the admin page are written once, so a later <c>generate-crud</c> brings them in line instead of
/// overwriting: a second entity reaches the sidebar, and a page from before permissions existed gets its guard.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiNavSidecarTests
{
    private readonly TemplateEngine _engine = new();

    private static ModuleModel Crud(string entity, string plural, string route, string? permission) => new()
    {
        RootNamespace = "Shop",
        ModuleNamespace = "Shop.Modules.Catalog",
        ModuleName = "Catalog",
        EntityName = entity,
        EntityNameLower = CodeGen.ToCamelCase(entity),
        RouteName = route,
        RequiredPermission = permission,
    };

    private string Sidecar(string entity, string plural, string route, string? permission)
        => _engine.Render("ui/UiModule", Crud(entity, plural, route, permission));

    // ── A second entity ──────────────────────────────────────────

    [Fact]
    public void A_second_entity_gets_its_own_sidebar_item_and_feature()
    {
        var first = Sidecar("Product", "Products", "products", "catalog:products:manage");

        var updated = UiNavSidecar.EnsureItem(first, "Catalog", "Categories", "categories", "catalog:categories:manage");

        updated.Should().Contain("\"Catalog.Products\"").And.Contain("\"Catalog.Categories\"");
        updated.Should().Contain("\"/catalog/categories\"").And.Contain("requiredPermission: \"catalog:categories:manage\"");
        updated.Should().Contain("[\"Products\", \"Categories\"]", "the manifest lists both features");
        updated.Should().Contain("requiredPermission: \"catalog:products:manage\"", "the first item is untouched");
        updated.IndexOf("Catalog.Categories", StringComparison.Ordinal)
            .Should().BeGreaterThan(updated.IndexOf("Catalog.Products", StringComparison.Ordinal), "items keep the order they were generated in");
    }

    [Fact]
    public void A_second_entity_in_a_host_without_permissions_gets_an_open_item()
    {
        var updated = UiNavSidecar.EnsureItem(Sidecar("Product", "Products", "products", null), "Catalog", "Categories", "categories", null);

        updated.Should().Contain("\"Catalog.Categories\"").And.NotContain("requiredPermission");
    }

    [Fact]
    public void The_sidecar_is_updated_once_and_stays_a_well_formed_call()
    {
        var first = Sidecar("Product", "Products", "products", "catalog:products:manage");
        var second = UiNavSidecar.EnsureItem(first, "Catalog", "Categories", "categories", "catalog:categories:manage");
        var third = UiNavSidecar.EnsureItem(second, "Catalog", "Suppliers", "suppliers", "catalog:suppliers:manage");

        UiNavSidecar.EnsureItem(third, "Catalog", "Categories", "categories", "catalog:categories:manage").Should().Be(third);
        third.Split(".AddItem(").Length.Should().Be(4, "three items");
        third.Count(c => c == '(').Should().Be(third.Count(c => c == ')'), "parentheses stay balanced");
        third.Should().Contain("[\"Products\", \"Categories\", \"Suppliers\"]");
    }

    [Fact]
    public void A_module_named_like_its_entity_still_lists_the_feature()
    {
        // "Orders" is both the module's name and the plural of the entity: the manifest name must not hide the missing feature.
        var first = _engine.Render("ui/UiModule", new ModuleModel
        {
            RootNamespace = "Shop",
            ModuleNamespace = "Shop.Modules.Orders",
            ModuleName = "Orders",
            EntityName = "Invoice",
            EntityNameLower = "invoice",
            RouteName = "invoices",
        });

        var updated = UiNavSidecar.EnsureItem(first, "Orders", "Orders", "orders", null);

        updated.Should().Contain("[\"Invoices\", \"Orders\"]");
    }

    [Fact]
    public void A_sidecar_that_no_longer_has_the_generated_shape_is_left_alone()
    {
        const string handWritten = "public sealed class CatalogUiModule : CustomUiModule { }";

        UiNavSidecar.EnsureItem(handWritten, "Catalog", "Categories", "categories", "catalog:categories:manage").Should().Be(handWritten);
    }

    // ── An item from before permissions existed ──────────────────

    [Fact]
    public void An_unguarded_item_gets_its_permission_once()
    {
        var old = Sidecar("Product", "Products", "products", null);

        var guarded = UiNavSidecar.EnsureItem(old, "Catalog", "Products", "products", "catalog:products:manage");

        guarded.Should().Contain("groupId: \"catalog\"").And.Contain("requiredPermission: \"catalog:products:manage\"");
        guarded.Split("requiredPermission").Length.Should().Be(2);
        UiNavSidecar.EnsureItem(guarded, "Catalog", "Products", "products", "catalog:products:manage").Should().Be(guarded);
    }

    [Fact]
    public void A_guarded_sidecar_matches_what_a_fresh_one_would_have_been()
    {
        var old = Sidecar("Product", "Products", "products", null);
        var fresh = Sidecar("Product", "Products", "products", "catalog:products:manage");

        UiNavSidecar.EnsureItem(old, "Catalog", "Products", "products", "catalog:products:manage")
            .ReplaceLineEndings("\n").Should().Be(fresh.ReplaceLineEndings("\n"));
    }

    // ── The page guard ───────────────────────────────────────────

    private string Page(string? permission)
        => _engine.Render("ui/CrudIndexPageModel", Crud("Product", "Products", "products", permission));

    [Fact]
    public void An_unguarded_page_gets_the_attribute_and_its_using()
    {
        var guarded = UiAccessGates.EnsurePageGuard(Page(null), "catalog:products:manage");

        guarded.Should().Contain("[Authorize(Policy = \"catalog:products:manage\")]")
            .And.Contain("using Microsoft.AspNetCore.Authorization;");
        guarded.IndexOf("[Authorize", StringComparison.Ordinal)
            .Should().BeLessThan(guarded.IndexOf("class IndexModel", StringComparison.Ordinal));
        guarded.IndexOf("[Authorize", StringComparison.Ordinal)
            .Should().BeGreaterThan(guarded.IndexOf("/// </summary>", StringComparison.Ordinal), "the attribute sits under the doc comment");
    }

    [Fact]
    public void A_guarded_page_is_the_same_as_a_fresh_one()
    {
        UiAccessGates.EnsurePageGuard(Page(null), "catalog:products:manage")
            .ReplaceLineEndings("\n").Should().Be(Page("catalog:products:manage").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_page_that_already_has_an_authorize_attribute_is_left_alone()
    {
        var ownGuard = UiAccessGates.EnsurePageGuard(Page(null), "shop:custom:manage");

        UiAccessGates.EnsurePageGuard(ownGuard, "catalog:products:manage").Should().Be(ownGuard);
        UiAccessGates.EnsurePageGuard("public class Something { }", "x:y:z").Should().Be("public class Something { }");
    }
}
