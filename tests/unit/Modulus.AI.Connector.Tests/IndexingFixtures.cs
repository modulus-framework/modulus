namespace Modulus.AI.Connector.Tests;

using System.Linq.Expressions;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.Entities;

/// <summary>The stored product the <c>Catalog.Product</c> index is built from (served by <see cref="GetProduct"/>).</summary>
[AiIndexed("Catalog.Product")]
public sealed class ProductRow
{
    public Guid Id { get; init; }
}

/// <summary>A stock line with generated Search and Calculate capabilities.</summary>
[AiQueryable("Catalog.Stock", "Stock lines per warehouse.", "catalog:read",
    Fields = ["Sku", "Quantity", "Price", "Cost", "Warehouse", "ReceivedOn"])]
public sealed class StockLine
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = "";

    public int Quantity { get; init; }

    public decimal Price { get; init; }

    [Classified(FieldClassification.Confidential)]
    public decimal Cost { get; init; }

    public string? Warehouse { get; init; }

    public DateOnly ReceivedOn { get; init; }

    /// <summary>Not listed in <see cref="AiQueryableAttribute.Fields"/>: never filtered on or returned.</summary>
    public string Notes { get; init; } = "internal";
}

/// <summary>An in-memory <see cref="IAiEntitySource"/>: runs the connector's expressions over lists.</summary>
public sealed class FakeEntitySource : IAiEntitySource
{
    public static readonly IReadOnlyList<StockLine> Stock =
    [
        new() { Id = Guid.Parse("00000000-0000-0000-0000-00000000000a"), Sku = "W-1", Quantity = 10, Price = 5m, Cost = 3m, Warehouse = "North", ReceivedOn = new(2026, 1, 5) },
        new() { Id = Guid.Parse("00000000-0000-0000-0000-00000000000b"), Sku = "W-2", Quantity = 2, Price = 8m, Cost = 4m, Warehouse = "North", ReceivedOn = new(2026, 2, 1) },
        new() { Id = Guid.Parse("00000000-0000-0000-0000-00000000000c"), Sku = "G-1", Quantity = 7, Price = 12m, Cost = 9m, Warehouse = "South", ReceivedOn = new(2026, 3, 9) },
        new() { Id = Guid.Parse("00000000-0000-0000-0000-00000000000d"), Sku = "G-2", Quantity = 1, Price = 20m, Cost = 15m, Warehouse = null, ReceivedOn = new(2026, 3, 20) },
    ];

    public AiEntityQuery? LastQuery { get; private set; }

    public AiAggregateQuery? LastAggregate { get; private set; }

    public IReadOnlyList<Type> EntityTypes => [typeof(ProductRow), typeof(StockLine)];

    public Task<IReadOnlyList<string>> ListKeysAsync(Type entityType, string? after, int take, CancellationToken ct = default)
    {
        IEnumerable<string> keys = entityType == typeof(ProductRow)
            ? Catalog.Products.Select(p => p.Id.ToString("D"))
            : Stock.Select(s => s.Id.ToString("D"));
        return Task.FromResult<IReadOnlyList<string>>(
            [.. keys.Order(StringComparer.Ordinal).Where(k => after is null || string.CompareOrdinal(k, after) > 0).Take(take)]);
    }

    public Task<IReadOnlyList<object>> ListAsync(Type entityType, AiEntityQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        IEnumerable<StockLine> rows = Filtered(query.Filter);
        IOrderedEnumerable<StockLine>? ordered = null;
        foreach (var sort in query.Sort)
        {
            var key = Compile(sort.Key);
            ordered = (ordered, sort.Descending) switch
            {
                (null, false) => rows.OrderBy(key, Comparer<object?>.Default),
                (null, true) => rows.OrderByDescending(key, Comparer<object?>.Default),
                (_, false) => ordered.ThenBy(key, Comparer<object?>.Default),
                _ => ordered.ThenByDescending(key, Comparer<object?>.Default),
            };
        }

        rows = ordered?.ThenBy(s => s.Id) ?? rows.OrderBy(s => s.Id);
        return Task.FromResult<IReadOnlyList<object>>([.. rows.Take(query.Take)]);
    }

    public Task<IReadOnlyList<AiAggregateRow>> AggregateAsync(Type entityType, AiAggregateQuery query, CancellationToken ct = default)
    {
        LastAggregate = query;
        var group = query.GroupBy is null ? (_ => null) : Compile(query.GroupBy);
        var value = query.Field is null ? (_ => null) : Compile(query.Field);
        var rows = Filtered(query.Filter)
            .GroupBy(group)
            .Select(g => new AiAggregateRow(g.Key, query.Aggregate switch
            {
                AiAggregate.Count => g.LongCount(),
                AiAggregate.Sum => g.Sum(r => Convert.ToDecimal(value(r), System.Globalization.CultureInfo.InvariantCulture)),
                AiAggregate.Average => g.Average(r => Convert.ToDecimal(value(r), System.Globalization.CultureInfo.InvariantCulture)),
                AiAggregate.Min => g.Min(value),
                _ => g.Max(value),
            }))
            .OrderByDescending(r => r.Value, Comparer<object?>.Default)
            .Take(query.MaxGroups);
        return Task.FromResult<IReadOnlyList<AiAggregateRow>>([.. rows]);
    }

    private static IEnumerable<StockLine> Filtered(LambdaExpression? filter)
        => filter is null ? Stock : Stock.Where(((Expression<Func<StockLine, bool>>)filter).Compile());

    private static Func<StockLine, object?> Compile(LambdaExpression selector)
    {
        var parameter = selector.Parameters[0];
        return Expression.Lambda<Func<StockLine, object?>>(Expression.Convert(selector.Body, typeof(object)), parameter).Compile();
    }
}

/// <summary>A change journal the tests fill by hand.</summary>
public sealed class FakeChangeFeed : IAiChangeFeed
{
    public List<AiJournalChange> Changes { get; } = [];

    public string Head { get; set; } = "h0";

    public (Guid TenantId, string? Cursor, int Max, DateTimeOffset SettledBefore)? LastRead { get; private set; }

    public Task<AiJournalPage> ReadAsync(Guid tenantId, string? cursor, int max, DateTimeOffset settledBefore, CancellationToken ct = default)
    {
        if (cursor == "bad")
            throw new FormatException("Invalid cursor.");
        LastRead = (tenantId, cursor, max, settledBefore);
        return Task.FromResult(new AiJournalPage([.. Changes], "next-cursor", HasMore: false));
    }

    public Task<string> GetHeadAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Head);
}
