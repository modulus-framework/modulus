using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the logic behind the component tag helpers (rendering is covered by the Tabler render tests).</summary>
[Trait("Category", "Unit")]
public sealed class ComponentLogicTests
{
    private static HttpRequest Request(string path, string query = "", string pathBase = "")
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        return http.Request;
    }

    // ---- PaginationUrls ----------------------------------------------------------

    [Fact]
    public void Build_sets_the_page_and_keeps_other_filters()
    {
        var url = PaginationUrls.Build(Request("/logs", "?Action=login&PageNumber=2&UserId=5"), "PageNumber", 3);

        url.Should().Be("/logs?Action=login&UserId=5&PageNumber=3");
    }

    [Fact]
    public void Build_matches_the_page_key_case_insensitively_and_always_drops_handler()
    {
        var url = PaginationUrls.Build(Request("/logs", "?pagenumber=9&handler=List&q=x"), "PageNumber", 1);

        url.Should().Be("/logs?q=x&PageNumber=1");
    }

    [Fact]
    public void Build_preserves_repeated_query_keys()
    {
        var url = PaginationUrls.Build(Request("/logs", "?tag=a&tag=b"), "PageNumber", 2);

        url.Should().Be("/logs?tag=a&tag=b&PageNumber=2");
    }

    [Fact]
    public void Build_layers_overrides_replacing_existing_keys_and_removing_null_ones()
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = "logout",
            ["Gone"] = null,
            ["Extra"] = "1",
        };

        var url = PaginationUrls.Build(Request("/logs", "?Action=login&Gone=yes&keep=1"), "PageNumber", 2, overrides);

        url.Should().Be("/logs?keep=1&Action=logout&Extra=1&PageNumber=2");
    }

    [Fact]
    public void Build_includes_the_path_base_and_encodes_values()
    {
        var url = PaginationUrls.Build(Request("/logs", "?name=a%26b%20c", pathBase: "/app"), "PageNumber", 2);

        url.Should().Be("/app/logs?name=a%26b%20c&PageNumber=2");
    }

    [Fact]
    public void Create_only_links_directions_that_exist()
    {
        var request = Request("/logs", "?PageNumber=2");
        var routes = new Dictionary<string, string?>();

        var both = PaginationTagHelper.Create(request, "Page", 2, true, true, "PageNumber", routes);
        var last = PaginationTagHelper.Create(request, "Page", 2, true, false, "PageNumber", routes);
        var first = PaginationTagHelper.Create(request, "Page", 1, false, true, "PageNumber", routes);

        both.PreviousUrl.Should().Be("/logs?PageNumber=1");
        both.NextUrl.Should().Be("/logs?PageNumber=3");
        last.NextUrl.Should().BeNull();
        first.PreviousUrl.Should().BeNull();
        first.Should().Match<PaginationModel>(m => m.Page == 1 && m.Label == "Page");
    }

    [Fact]
    public void Build_rejects_a_blank_page_parameter()
    {
        ((Action)(() => PaginationUrls.Build(Request("/x"), " ", 1))).Should().Throw<ArgumentException>();
    }

    // ---- breadcrumbs -------------------------------------------------------------

    private sealed class Root : IBreadcrumbContributor
    {
        public ValueTask ConfigureAsync(BreadcrumbContext context, CancellationToken cancellationToken = default)
        {
            context.Add("Home", "/");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Leaf : IBreadcrumbContributor
    {
        public ValueTask ConfigureAsync(BreadcrumbContext context, CancellationToken cancellationToken = default)
        {
            if (context.PageId == "Users.Index")
            {
                context.Add("Users");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Rewrite : IBreadcrumbContributor
    {
        public ValueTask ConfigureAsync(BreadcrumbContext context, CancellationToken cancellationToken = default)
        {
            // Contributors run in order and may reshape what earlier ones added.
            if (context.Items.Count > 0)
            {
                context.Items[0] = new BreadcrumbItem("Start", "/start");
            }

            return ValueTask.CompletedTask;
        }
    }

    private static IBreadcrumbProvider Provider(params Type[] contributors)
    {
        var services = new ServiceCollection();
        foreach (var type in contributors)
        {
            services.AddScoped(typeof(IBreadcrumbContributor), type);
        }

        services.AddModulusUi();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IBreadcrumbProvider>();
    }

    [Fact]
    public void Provider_merges_contributors_in_registration_order_and_lets_later_ones_rewrite()
    {
        var trail = Provider(typeof(Root), typeof(Leaf), typeof(Rewrite)).GetItems("Users.Index");

        trail.Select(i => i.Title).Should().Equal("Start", "Users");
        trail[0].Url.Should().Be("/start");
    }

    [Fact]
    public void Provider_returns_only_what_contributors_supply_for_the_page()
    {
        Provider(typeof(Leaf)).GetItems("Orders.Index").Should().BeEmpty();
    }

    [Fact]
    public void Provider_rejects_a_blank_page_id_and_context_rejects_a_blank_title()
    {
        ((Action)(() => Provider().GetItems(" "))).Should().Throw<ArgumentException>();
        ((Action)(() => new BreadcrumbContext("p").Add(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddBreadcrumbContributor_is_idempotent_per_type()
    {
        var services = new ServiceCollection();

        services.AddBreadcrumbContributor<Root>();
        services.AddBreadcrumbContributor<Root>();

        services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetServices<IBreadcrumbContributor>().Should().ContainSingle();
    }

    // ---- ViewData helpers --------------------------------------------------------

    private static ViewDataDictionary ViewData() => new(new EmptyModelMetadataProvider(), new ModelStateDictionary());

    [Fact]
    public void PageId_round_trips_and_is_null_when_unset()
    {
        var viewData = ViewData();
        viewData.GetPageId().Should().BeNull();

        viewData.SetPageId("Catalog.Products.Index");

        viewData.GetPageId().Should().Be("Catalog.Products.Index");
        ((Action)(() => viewData.SetPageId(""))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Breadcrumbs_round_trip_in_order_and_are_null_when_unset()
    {
        var viewData = ViewData();
        viewData.GetBreadcrumbs().Should().BeNull();

        viewData.SetBreadcrumbs(new BreadcrumbItem("Home", "/"), new BreadcrumbItem("Users"));

        viewData.GetBreadcrumbs()!.Select(i => i.Title).Should().Equal("Home", "Users");
    }

    [Fact]
    public void AddModulusUi_registers_the_view_resolver_and_breadcrumb_provider_without_a_theme()
    {
        var services = new ServiceCollection();

        services.AddModulusUi();

        services.Should().Contain(d => d.ServiceType == typeof(IModulusViewResolver));
        services.Should().Contain(d => d.ServiceType == typeof(IBreadcrumbProvider));
    }
}
