using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for contributed list columns and row actions (the list side of the entity UI registry).</summary>
[Trait("Category", "Unit")]
public sealed class EntityListContributionTests
{
    /// <summary>Records every batch it is asked for, so tests can prove "one call per page".</summary>
    private sealed class StockProvider : IEntityColumnValueProvider
    {
        public List<string[]> Batches { get; } = [];

        public Task<IReadOnlyDictionary<string, string?>> LoadAsync(IReadOnlyCollection<string> rowIds, CancellationToken cancellationToken = default)
        {
            Batches.Add([.. rowIds]);
            return Task.FromResult<IReadOnlyDictionary<string, string?>>(
                rowIds.Where(id => id != "3").ToDictionary(id => id, id => (string?)$"stock-{id}"));
        }
    }

    private sealed class NotAProvider;

    private static ICurrentUser User(params string[] permissions)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
        return user;
    }

    private static (IEntityUiRegistry Registry, IServiceProvider Services, StockProvider Provider) Build(params Action<IServiceCollection>[] modules)
    {
        var provider = new StockProvider();
        var services = new ServiceCollection();
        services.AddSingleton(provider);
        foreach (var module in modules)
        {
            module(services);
        }

        var built = services.BuildServiceProvider();
        return (built.GetRequiredService<IEntityUiRegistry>(), built, provider);
    }

    private static readonly Action<IServiceCollection> Inventory = s => s.ConfigureEntityUi("Catalog.Product", e =>
    {
        e.Columns.Add(new EntityColumn("Stock", "Stock", typeof(StockProvider), order: 45, cssClass: "text-end"));
        e.Columns.Add(new EntityColumn("Cost", "Cost", typeof(StockProvider), order: 10, requiredPermission: "finance.view"));
        e.Actions.Add(new EntityAction("Inventory.Adjust", "Adjust stock", hxGet: "/Inventory/Adjust?productId={id}", order: 20, requiredPermission: "inventory.adjust"));
        e.Actions.Add(new EntityAction("Inventory.History", "History", url: "/Inventory/History/{id}", order: 10));
    });

    [Fact]
    public void Columns_and_actions_are_permission_filtered_and_ordered()
    {
        var (registry, _, _) = Build(Inventory);

        registry.GetVisibleColumns("Catalog.Product", User()).Select(c => c.Name).Should().Equal("Stock");
        registry.GetVisibleColumns("Catalog.Product", User("finance.view")).Select(c => c.Name).Should().Equal("Cost", "Stock");
        registry.GetVisibleActions("Catalog.Product", User()).Select(a => a.Id).Should().Equal("Inventory.History");
        registry.GetVisibleActions("Catalog.Product", User("inventory.adjust")).Select(a => a.Id).Should().Equal("Inventory.History", "Inventory.Adjust");
    }

    [Fact]
    public void Later_modules_can_remove_columns_and_actions_and_duplicates_are_rejected()
    {
        var (registry, _, _) = Build(
            Inventory,
            s => s.ConfigureEntityUi("Catalog.Product", e =>
            {
                e.Columns.Remove("stock").Should().BeTrue();
                e.Actions.Remove("Inventory.History").Should().BeTrue();
            }));

        registry.GetVisibleColumns("Catalog.Product", User("finance.view")).Select(c => c.Name).Should().Equal("Cost");
        registry.GetVisibleActions("Catalog.Product", User("inventory.adjust")).Select(a => a.Id).Should().Equal("Inventory.Adjust");

        var (duplicate, _, _) = Build(
            Inventory,
            s => s.ConfigureEntityUi("Catalog.Product", e => e.Actions.Add(new EntityAction("inventory.adjust", "Again", url: "/x"))));
        var act = () => duplicate.GetVisibleActions("Catalog.Product", User());
        act.Should().Throw<InvalidOperationException>().WithMessage("*action 'inventory.adjust'*");
    }

    [Fact]
    public void An_action_defines_exactly_one_target_and_expands_the_row_id_url_encoded()
    {
        var none = () => new EntityAction("A", "A");
        var two = () => new EntityAction("A", "A", url: "/a", hxGet: "/b");
        none.Should().Throw<ArgumentException>().WithMessage("*exactly one*");
        two.Should().Throw<ArgumentException>().WithMessage("*exactly one*");

        // {id} is user data: it must not be able to break out of the URL.
        var action = new EntityAction("A", "A", hxGet: "/p/{id}?x={id}");
        EntityAction.Expand(action.HxGet!, "a b/c&d").Should().Be("/p/a%20b%2Fc%26d?x=a%20b%2Fc%26d");
    }

    [Fact]
    public void A_column_needs_a_concrete_value_provider_type()
    {
        var act = () => new EntityColumn("Stock", "Stock", typeof(NotAProvider));
        var abstractType = () => new EntityColumn("Stock", "Stock", typeof(IEntityColumnValueProvider));

        act.Should().Throw<ArgumentException>().WithMessage("*implementing IEntityColumnValueProvider*");
        abstractType.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Column_values_load_with_one_provider_call_per_column_for_the_whole_page()
    {
        var (registry, services, provider) = Build(Inventory);

        var values = await registry.LoadEntityColumnsAsync(services, "Catalog.Product", User(), ["1", "2", "3", "2"]);

        provider.Batches.Should().ContainSingle().Which.Should().Equal("1", "2", "3");
        values.Get("Stock", "1").Should().Be("stock-1");
        values.Get("Stock", "3").Should().BeNull("the provider had nothing for that row");
        values.Get("Cost", "1").Should().BeNull("the user may not see Cost, so it was never loaded");
    }

    [Fact]
    public async Task Providers_are_created_when_not_registered_and_nothing_loads_for_no_rows_or_no_columns()
    {
        var services = new ServiceCollection();
        services.ConfigureEntityUi("Catalog.Product", e => e.Columns.Add(new EntityColumn("Stock", "Stock", typeof(StockProvider))));
        var built = services.BuildServiceProvider();
        var registry = built.GetRequiredService<IEntityUiRegistry>();

        (await registry.LoadEntityColumnsAsync(built, "Catalog.Product", User(), ["7"])).Get("Stock", "7").Should().Be("stock-7");
        (await registry.LoadEntityColumnsAsync(built, "Catalog.Product", User(), [])).Get("Stock", "7").Should().BeNull();
        (await registry.LoadEntityColumnsAsync(built, "Sales.Order", User(), ["7"])).Should().BeSameAs(EntityColumnValues.Empty);
    }
}
