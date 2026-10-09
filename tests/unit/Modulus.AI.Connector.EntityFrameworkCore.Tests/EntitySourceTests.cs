namespace Modulus.AI.Connector.EntityFrameworkCore.Tests;

using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class EntitySourceTests : IAsyncLifetime
{
    private readonly JournalTestHost _host = new();

    public async Task InitializeAsync()
    {
        await _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            await db.Products.AddRangeAsync(
                new Product { Name = "Bolt", Category = "Hardware", Quantity = 100, Price = 0.25m, Weight = 0.01 },
                new Product { Name = "Nut", Category = "Hardware", Quantity = 300, Price = 0.10m, Weight = 0.005 },
                new Product { Name = "Drill", Category = "Tools", Quantity = 3, Price = 89.90m, Weight = 1.5 },
                new Product { Name = "Saw", Category = "Tools", Quantity = 5, Price = 24.50m, Weight = 0.8 },
                new Product { Name = "Mystery", Category = null, Quantity = 1, Price = 1m, Weight = 0.1 },
                new Product { Name = "Gone", Category = "Tools", Quantity = 9, Price = 999m, IsDeleted = true });
            db.Notes.Add(new Note { Text = "x" });
            await db.SaveChangesAsync();
        });
        await _host.InAsync(JournalTestHost.CompanyB, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(new Product { Name = "Foreign", Category = "Tools", Quantity = 1000, Price = 5000m });
            await db.SaveChangesAsync();
        });
        await _host.InAsync(null, async sp =>
        {
            var db = sp.GetRequiredService<SalesDbContext>();
            await db.Orders.AddRangeAsync(new Order { Id = "SO-3" }, new Order { Id = "SO-1" }, new Order { Id = "SO-2" });
            await db.SaveChangesAsync();
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> InCompanyA<T>(Func<EfAiEntitySource, Task<T>> work)
        => _host.InAsync(JournalTestHost.CompanyA, sp => work(sp.GetRequiredService<EfAiEntitySource>()));

    [Fact]
    public async Task Entity_types_are_the_mapped_entities_without_the_journal()
    {
        var types = await InCompanyA(source => Task.FromResult(source.EntityTypes));

        types.Should().Contain([typeof(Product), typeof(Counter), typeof(Note), typeof(Order)]);
        types.Should().NotContain(typeof(AiChangeRecord));
    }

    [Fact]
    public async Task Guid_keys_page_through_the_company_without_gaps_or_repeats()
    {
        var keys = new List<string>();
        string? after = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await InCompanyA(source => source.ListKeysAsync(typeof(Product), after, 2));
            if (page.Count == 0)
                break;
            keys.AddRange(page);
            after = page[^1];
        }

        var expected = await InCompanyA(source => source.ListKeysAsync(typeof(Product), null, 100));
        keys.Should().Equal(expected);
        keys.Should().HaveCount(5, "the soft-deleted row and the other company's row are filtered out");
        keys.Should().OnlyHaveUniqueItems();
        keys.Where(k => !Guid.TryParse(k, out _) || k != k.ToLowerInvariant()).Should().BeEmpty();
    }

    [Fact]
    public async Task Composite_keys_page_in_key_order_without_gaps_or_repeats()
    {
        await _host.InAsync(null, async sp =>
        {
            var db = sp.GetRequiredService<SalesDbContext>();
            await db.OrderLines.AddRangeAsync(
                new OrderLine { OrderId = "B", LineNo = 1 }, new OrderLine { OrderId = "A", LineNo = 10 },
                new OrderLine { OrderId = "A", LineNo = 2 }, new OrderLine { OrderId = "A|x", LineNo = 1 },
                new OrderLine { OrderId = "C", LineNo = 3 });
            await db.SaveChangesAsync();
        });

        var keys = new List<string>();
        string? after = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await _host.InAsync(null, sp => sp.GetRequiredService<EfAiEntitySource>().ListKeysAsync(typeof(OrderLine), after, 2));
            if (page.Count == 0)
                break;
            keys.AddRange(page);
            after = page[^1];
        }

        // Ordinal column order: "A" < "A|x" < "B" < "C", and within one order by line number.
        keys.Select(AiCompositeKey.Split).Select(p => (p[0], int.Parse(p[1]))).Should().Equal(
            ("A", 2), ("A", 10), ("A|x", 1), ("B", 1), ("C", 3));
        var bad = () => _host.InAsync(null, sp => sp.GetRequiredService<EfAiEntitySource>().ListKeysAsync(typeof(OrderLine), "only-one-part", 2));
        await bad.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task String_keys_page_in_order()
    {
        var first = await InCompanyA(source => source.ListKeysAsync(typeof(Order), null, 2));
        var rest = await InCompanyA(source => source.ListKeysAsync(typeof(Order), first[^1], 2));

        first.Should().Equal("SO-1", "SO-2");
        rest.Should().Equal("SO-3");
    }

    [Fact]
    public async Task List_filters_with_parameters_sorts_and_takes()
    {
        var minimum = 3;
        Expression<Func<Product, bool>> filter = p => p.Quantity >= minimum && p.Category != null;
        Expression<Func<Product, string?>> byCategory = p => p.Category;
        Expression<Func<Product, int>> byQuantity = p => p.Quantity;

        var rows = await InCompanyA(source => source.ListAsync(typeof(Product),
            new AiEntityQuery(filter, [new AiEntitySort(byCategory, false), new AiEntitySort(byQuantity, true)], 3)));

        rows.Cast<Product>().Select(p => p.Name).Should().Equal("Nut", "Bolt", "Saw");
    }

    [Fact]
    public async Task Count_without_a_group_is_one_total()
    {
        var rows = await InCompanyA(source => source.AggregateAsync(typeof(Product),
            new AiAggregateQuery(null, null, AiAggregate.Count, null, 10)));

        rows.Should().ContainSingle().Which.Should().Be(new AiAggregateRow(null, 5L));
    }

    [Fact]
    public async Task Sum_is_grouped_and_ordered_largest_first()
    {
        Expression<Func<Product, string?>> group = p => p.Category;
        Expression<Func<Product, int>> quantity = p => p.Quantity;

        var rows = await InCompanyA(source => source.AggregateAsync(typeof(Product),
            new AiAggregateQuery(null, group, AiAggregate.Sum, quantity, 10)));

        rows.Should().Equal(new AiAggregateRow("Hardware", 400L), new AiAggregateRow("Tools", 8L), new AiAggregateRow(null, 1L));
    }

    [Fact]
    public async Task The_number_of_groups_is_capped()
    {
        Expression<Func<Product, string?>> group = p => p.Category;

        var rows = await InCompanyA(source => source.AggregateAsync(typeof(Product),
            new AiAggregateQuery(null, group, AiAggregate.Count, null, 1)));

        rows.Should().ContainSingle().Which.Value.Should().Be(2L);
    }

#if NET8_0
    [Fact(Skip = "EF Core 8's SQLite provider cannot Sum/Average decimal columns; the other providers can.")]
#else
    [Fact]
#endif
    public async Task Decimal_and_double_aggregates_translate()
    {
        Expression<Func<Product, bool>> tools = p => p.Category == "Tools";
        Expression<Func<Product, decimal>> price = p => p.Price;
        Expression<Func<Product, double>> weight = p => p.Weight;

        var sum = await Aggregate(tools, AiAggregate.Sum, price);
        var average = await Aggregate(tools, AiAggregate.Average, price);
        var max = await Aggregate(tools, AiAggregate.Max, price);
        var min = await Aggregate(tools, AiAggregate.Min, weight);
        var weights = await Aggregate(tools, AiAggregate.Sum, weight);

        sum.Should().Be(114.40m);
        average.Should().Be(57.20m);
        max.Should().Be(89.90m);
        min.Should().Be(0.8);
        ((double)weights!).Should().BeApproximately(2.3, 1e-9);
    }

#if NET8_0
    [Fact(Skip = "EF Core 8's SQLite provider cannot Sum/Average decimal columns; the other providers can.")]
#else
    [Fact]
#endif
    public async Task An_aggregate_over_no_rows_answers_zero_or_null()
    {
        Expression<Func<Product, bool>> nothing = p => p.Quantity > 1_000_000;
        Expression<Func<Product, decimal>> price = p => p.Price;

        var count = await InCompanyA(source => source.AggregateAsync(typeof(Product), new AiAggregateQuery(nothing, null, AiAggregate.Count, null, 10)));
        var sum = await Aggregate(nothing, AiAggregate.Sum, price);

        count.Should().Equal(new AiAggregateRow(null, 0L));
        sum.Should().BeNull();
    }

    [Fact]
    public async Task An_unmapped_type_is_refused()
    {
        var call = () => InCompanyA(source => source.ListKeysAsync(typeof(string), null, 1));

        await call.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<object?> Aggregate(LambdaExpression filter, AiAggregate aggregate, LambdaExpression field)
        => (await InCompanyA(source => source.AggregateAsync(typeof(Product), new AiAggregateQuery(filter, null, aggregate, field, 10))))
            .Single().Value;
}
