namespace Modulus.EFCore.Integration.Tests;

using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Modulus.AI.Connector;
using Modulus.AI.Connector.Data;
using Modulus.AI.Connector.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class AiStock
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = "";

    public string? Warehouse { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public double Weight { get; set; }

    public DateTime ReceivedAt { get; set; }
}

public sealed class AiOrderLine
{
    public string OrderId { get; set; } = "";

    public int LineNo { get; set; }

    public decimal Amount { get; set; }
}

public sealed class AiProviderDbContext(DbContextOptions<AiProviderDbContext> options) : DbContext(options)
{
    public DbSet<AiStock> Stock => Set<AiStock>();

    public DbSet<AiOrderLine> Lines => Set<AiOrderLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AiStock>().Property(s => s.Price).HasPrecision(18, 4);
        builder.Entity<AiOrderLine>().HasKey(l => new { l.OrderId, l.LineNo });
        builder.Entity<AiOrderLine>().Property(l => l.Amount).HasPrecision(18, 4);
        builder.Entity<AiOrderLine>().Property(l => l.OrderId).HasMaxLength(64);
    }
}

/// <summary>
/// The connector's generated queries (filters, sorts, aggregates, keyset paging, composite keys) are LINQ expressions the entity source turns
/// into SQL. SQLite runs them in the unit tests; these run the same shapes on the databases apps deploy to.
/// </summary>
[Trait("Category", "Integration")]
public abstract class AiEntitySourceProviderTests
{
    private static readonly Guid[] Ids =
        [.. Enumerable.Range(1, 6).Select(i => Guid.Parse($"{i:x8}-0000-4000-8000-00000000000{i}"))];

    // Npgsql writes timestamptz only from UTC DateTimes, so every date in these tests is UTC.
    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    protected abstract DbContextOptions<AiProviderDbContext> Options { get; }

    private async Task<EfAiEntitySource> SeededAsync()
    {
        await using (var seed = new AiProviderDbContext(Options))
        {
            // The schema is created once per container and emptied before each test. Dropping the
            // database is not an option: earlier tests' pooled connections keep it open (PostgreSQL),
            // and the SQL Server container's connection string targets master (SINGLE_USER fails).
            await seed.Database.EnsureCreatedAsync();
            await seed.Stock.ExecuteDeleteAsync();
            await seed.Lines.ExecuteDeleteAsync();
            await seed.Stock.AddRangeAsync(
                new AiStock { Id = Ids[0], Sku = "W-1", Warehouse = "North", Quantity = 10, Price = 5.5m, Weight = 0.5, ReceivedAt = Utc(2026, 1, 5) },
                new AiStock { Id = Ids[1], Sku = "W-2", Warehouse = "North", Quantity = 2, Price = 8m, Weight = 1.25, ReceivedAt = Utc(2026, 2, 1) },
                new AiStock { Id = Ids[2], Sku = "G-1", Warehouse = "South", Quantity = 7, Price = 12.25m, Weight = 2, ReceivedAt = Utc(2026, 3, 9) },
                new AiStock { Id = Ids[3], Sku = "G-2", Warehouse = null, Quantity = 1, Price = 20m, Weight = 0.75, ReceivedAt = Utc(2026, 3, 20) },
                new AiStock { Id = Ids[4], Sku = "X-1", Warehouse = "South", Quantity = 4, Price = 1m, Weight = 3, ReceivedAt = Utc(2026, 4, 2) },
                new AiStock { Id = Ids[5], Sku = "X-2", Warehouse = "North", Quantity = 6, Price = 3m, Weight = 4, ReceivedAt = Utc(2026, 4, 3) });
            await seed.Lines.AddRangeAsync(
                new AiOrderLine { OrderId = "B", LineNo = 1, Amount = 1m }, new AiOrderLine { OrderId = "A", LineNo = 10, Amount = 2m },
                new AiOrderLine { OrderId = "A", LineNo = 2, Amount = 3m }, new AiOrderLine { OrderId = "C", LineNo = 3, Amount = 4m },
                new AiOrderLine { OrderId = "A", LineNo = 11, Amount = 5m });
            await seed.SaveChangesAsync();
        }

        return new EfAiEntitySource([new AiProviderDbContext(Options)]);
    }

    private static async Task<object?> Value(EfAiEntitySource source, LambdaExpression? filter, AiAggregate aggregate, LambdaExpression? field)
        => (await source.AggregateAsync(typeof(AiStock), new AiAggregateQuery(filter, null, aggregate, field, 10))).Single().Value;

    [Fact]
    public async Task Grouped_sums_and_counts_translate_and_order_largest_first()
    {
        var source = await SeededAsync();
        Expression<Func<AiStock, string?>> group = s => s.Warehouse;
        Expression<Func<AiStock, int>> quantity = s => s.Quantity;

        var sums = await source.AggregateAsync(typeof(AiStock), new AiAggregateQuery(null, group, AiAggregate.Sum, quantity, 10));
        var counts = await source.AggregateAsync(typeof(AiStock), new AiAggregateQuery(null, group, AiAggregate.Count, null, 1));

        sums.Should().Equal(new AiAggregateRow("North", 18L), new AiAggregateRow("South", 11L), new AiAggregateRow(null, 1L));
        counts.Should().ContainSingle().Which.Value.Should().Be(3L, "the group cap keeps the largest group");
    }

    [Fact]
    public async Task Decimal_and_double_aggregates_and_empty_results()
    {
        var source = await SeededAsync();
        Expression<Func<AiStock, bool>> north = s => s.Warehouse == "North";
        Expression<Func<AiStock, bool>> nothing = s => s.Quantity > 1_000_000;
        Expression<Func<AiStock, decimal>> price = s => s.Price;
        Expression<Func<AiStock, double>> weight = s => s.Weight;

        (await Value(source, north, AiAggregate.Sum, price)).Should().Be(16.5m);
        (await Value(source, north, AiAggregate.Max, price)).Should().Be(8m);
        (await Value(source, north, AiAggregate.Min, weight)).Should().Be(0.5);
        ((decimal)(await Value(source, north, AiAggregate.Average, price))!).Should().BeApproximately(5.5m, 0.0001m);
        ((double)(await Value(source, north, AiAggregate.Sum, weight))!).Should().BeApproximately(5.75, 1e-9);
        (await Value(source, nothing, AiAggregate.Count, null)).Should().Be(0L);
        (await Value(source, nothing, AiAggregate.Sum, price)).Should().BeNull();
    }

    [Fact]
    public async Task A_search_filters_sorts_and_takes()
    {
        var source = await SeededAsync();
        Expression<Func<AiStock, bool>> filter = s => s.Quantity >= 4 && s.Sku.StartsWith("W") || s.Warehouse == null;
        Expression<Func<AiStock, decimal>> byPrice = s => s.Price;

        var rows = await source.ListAsync(typeof(AiStock), new AiEntityQuery(filter, [new AiEntitySort(byPrice, Descending: true)], 10));

        rows.Cast<AiStock>().Select(s => s.Sku).Should().Equal("G-2", "W-1");
    }

    [Fact]
    public async Task Date_filters_translate()
    {
        var source = await SeededAsync();
        var from = Utc(2026, 3, 1);
        Expression<Func<AiStock, bool>> filter = s => s.ReceivedAt >= from;
        Expression<Func<AiStock, DateTime>> byDate = s => s.ReceivedAt;

        var rows = await source.ListAsync(typeof(AiStock), new AiEntityQuery(filter, [new AiEntitySort(byDate, Descending: false)], 10));

        rows.Cast<AiStock>().Select(s => s.Sku).Should().Equal("G-1", "G-2", "X-1", "X-2");
    }

    [Fact]
    public async Task Guid_keys_page_without_gaps_or_repeats()
    {
        var source = await SeededAsync();
        var keys = new List<string>();
        string? after = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await source.ListKeysAsync(typeof(AiStock), after, 2);
            if (page.Count == 0)
                break;
            keys.AddRange(page);
            after = page[^1];
        }

        keys.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        keys.Order(StringComparer.Ordinal).Should().BeEquivalentTo(Ids.Select(i => i.ToString("D")), "every id is paged exactly once, in the database's own key order");
    }

    [Fact]
    public async Task Composite_keys_page_in_key_order()
    {
        var source = await SeededAsync();
        var keys = new List<string>();
        string? after = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await source.ListKeysAsync(typeof(AiOrderLine), after, 2);
            if (page.Count == 0)
                break;
            keys.AddRange(page);
            after = page[^1];
        }

        keys.Select(AiCompositeKey.Split).Select(p => (p[0], int.Parse(p[1]))).Should().HaveCount(5).And.OnlyHaveUniqueItems();
        keys.Select(AiCompositeKey.Split).Select(p => p[0]).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AiEntitySourcePostgreSqlTests : AiEntitySourceProviderTests, IClassFixture<AiEntitySourcePostgreSqlTests.Db>
{
    public sealed class Db : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

        public string ConnectionString => _container.GetConnectionString();

        public Task InitializeAsync() => _container.StartAsync();

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();
    }

    private readonly Db _db;

    public AiEntitySourcePostgreSqlTests(Db db) => _db = db;

    protected override DbContextOptions<AiProviderDbContext> Options
        => new DbContextOptionsBuilder<AiProviderDbContext>().UseNpgsql(_db.ConnectionString).Options;
}

public sealed class AiEntitySourceMySqlTests : AiEntitySourceProviderTests, IClassFixture<AiEntitySourceMySqlTests.Db>
{
    public sealed class Db : IAsyncLifetime
    {
        private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").WithUsername("root").WithPassword("root").Build();

        public string ConnectionString => _container.GetConnectionString();

        public Task InitializeAsync() => _container.StartAsync();

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();
    }

    private readonly Db _db;

    public AiEntitySourceMySqlTests(Db db) => _db = db;

    protected override DbContextOptions<AiProviderDbContext> Options
        => new DbContextOptionsBuilder<AiProviderDbContext>().UseMySQL(_db.ConnectionString).Options;
}

public sealed class AiEntitySourceSqlServerTests : AiEntitySourceProviderTests, IClassFixture<AiEntitySourceSqlServerTests.Db>
{
    public sealed class Db : IAsyncLifetime
    {
        private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public string ConnectionString => _container.GetConnectionString();

        public Task InitializeAsync() => _container.StartAsync();

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();
    }

    private readonly Db _db;

    public AiEntitySourceSqlServerTests(Db db) => _db = db;

    protected override DbContextOptions<AiProviderDbContext> Options
        => new DbContextOptionsBuilder<AiProviderDbContext>().UseSqlServer(_db.ConnectionString).Options;
}
