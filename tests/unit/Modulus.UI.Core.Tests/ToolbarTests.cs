using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for page toolbars: validation, contributor composition, permission filtering, ordering.</summary>
[Trait("Category", "Unit")]
public sealed class ToolbarTests
{
    private sealed class ExportButton : IToolbarContributor
    {
        public ValueTask ConfigureAsync(ToolbarContext context, CancellationToken cancellationToken = default)
        {
            if (context.PageId == "Catalog.Products.Index")
            {
                context.Add(new ToolbarItem("Reports.Export", "Export", HxGet: "/reports/export", Icon: "download",
                    RequiredPermission: "reports:export", Order: 50));
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CreateButtons : IToolbarContributor
    {
        public ValueTask ConfigureAsync(ToolbarContext context, CancellationToken cancellationToken = default)
        {
            context.Add(new ToolbarItem("Catalog.New", "New", Url: "/catalog/new", Order: 10));
            context.Add(new ToolbarItem("Catalog.Import", "Import", HxPost: "/catalog/import", Order: 50));
            return ValueTask.CompletedTask;
        }
    }

    private static IToolbarProvider Provider(bool granted, params Type[] contributors)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(granted);

        var services = new ServiceCollection();
        services.AddScoped(_ => user);
        services.AddModulusUi();
        foreach (var type in contributors)
        {
            services.AddScoped(typeof(IToolbarContributor), type);
        }

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IToolbarProvider>();
    }

    [Fact]
    public void Add_requires_id_title_and_exactly_some_target()
    {
        var context = new ToolbarContext("Any");

        ((Action)(() => context.Add(new ToolbarItem("", "T", Url: "/x")))).Should().Throw<ArgumentException>();
        ((Action)(() => context.Add(new ToolbarItem("i", " ", Url: "/x")))).Should().Throw<ArgumentException>();
        ((Action)(() => context.Add(new ToolbarItem("i", "T")))).Should().Throw<ArgumentException>()
            .WithMessage("*Url, HxGet or HxPost*");
        context.Items.Should().BeEmpty();
    }

    [Fact]
    public void Add_accepts_each_target_kind()
    {
        var context = new ToolbarContext("Any");

        context.Add(new ToolbarItem("a", "A", Url: "/a"))
            .Add(new ToolbarItem("b", "B", HxGet: "/b"))
            .Add(new ToolbarItem("c", "C", HxPost: "/c"));

        context.Items.Select(i => i.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Provider_passes_the_page_id_so_contributors_can_target_other_modules_pages()
    {
        var provider = Provider(granted: true, typeof(ExportButton));

        provider.GetItems("Catalog.Products.Index").Should().ContainSingle().Which.Id.Should().Be("Reports.Export");
        provider.GetItems("Orders.Index").Should().BeEmpty();
    }

    [Fact]
    public void Provider_hides_items_the_user_lacks_permission_for()
    {
        Provider(granted: false, typeof(ExportButton)).GetItems("Catalog.Products.Index").Should().BeEmpty();
    }

    [Fact]
    public void Provider_merges_contributors_sorted_by_order_then_title()
    {
        var provider = Provider(granted: true, typeof(ExportButton), typeof(CreateButtons));

        provider.GetItems("Catalog.Products.Index").Select(i => i.Id)
            .Should().Equal("Catalog.New", "Reports.Export", "Catalog.Import");
    }

    [Fact]
    public void Provider_rejects_a_blank_page_id()
    {
        var provider = Provider(granted: true);

        ((Action)(() => provider.GetItems(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddToolbarContributor_is_idempotent_per_type()
    {
        var services = new ServiceCollection();

        services.AddToolbarContributor<ExportButton>();
        services.AddToolbarContributor<ExportButton>();

        services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetServices<IToolbarContributor>().Should().ContainSingle();
    }
}
