namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

// ModuleDbContext maps IHasExtraProperties.ExtraProperties as a JSON text column for every implementing
// entity. Proves the wiring: values round-trip, the column really is JSON, an in-place edit of the dictionary
// is detected as a change, and a fresh entity is stored as "{}" and read back as an empty (never null) bag.
[Trait("Category", "Unit")]
public sealed class ExtraPropertiesTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;

    public ExtraPropertiesTests()
    {
        _connection = new SqliteConnection("DataSource=extra-props-mem;Mode=Memory;Cache=Shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, NullCurrentTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<CatalogDbContext>(o => o.UseSqlite(_connection));
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Text_values_round_trip_and_the_column_holds_json()
    {
        var id = Guid.NewGuid();
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = new Product { Id = id, Name = "Widget" };
            product.SetExtraProperties(new Dictionary<string, string?>
            {
                ["ReorderLevel"] = "25.5",
                ["Note"] = "quote \" and ünïcode ✓",
            });
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        (await ReadRawExtraAsync(id)).Should().StartWith("{").And.Contain("\"ReorderLevel\"");

        await using var readScope = _root.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.SingleAsync(p => p.Id == id);
        loaded.GetExtraProperty("ReorderLevel").Should().Be("25.5");
        loaded.GetExtraProperty("Note").Should().Be("quote \" and ünïcode ✓");
        loaded.GetExtraProperty("Missing").Should().BeNull();
    }

    [Fact]
    public async Task Changing_an_entry_in_place_is_detected_and_saved()
    {
        var id = Guid.NewGuid();
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = new Product { Id = id, Name = "Widget" };
            product.ExtraProperties["Bin"] = "A-1";
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == id);
            product.ExtraProperties["Bin"] = "B-9";   // no property assignment, only a mutation of the bag
            product.ExtraProperties["Zone"] = "North";
            await db.SaveChangesAsync();
        }

        await using var readScope = _root.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.SingleAsync(p => p.Id == id);
        loaded.ExtraProperties.Should().Equal(new Dictionary<string, string?> { ["Bin"] = "B-9", ["Zone"] = "North" });
    }

    [Fact]
    public async Task A_new_entity_is_stored_as_an_empty_object_and_reads_back_as_an_empty_bag()
    {
        var id = Guid.NewGuid();
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Products.Add(new Product { Id = id, Name = "Plain" });
            await db.SaveChangesAsync();
        }

        (await ReadRawExtraAsync(id)).Should().Be("{}");

        await using var readScope = _root.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.SingleAsync(p => p.Id == id);
        loaded.ExtraProperties.Should().NotBeNull().And.BeEmpty();
    }

    private async Task<string?> ReadRawExtraAsync(Guid id)
    {
        await using var scope = _root.CreateAsyncScope();
        var table = scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Model.FindEntityType(typeof(Product))!.GetTableName();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT \"ExtraProperties\" FROM \"{table}\" WHERE \"Id\" = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class Product : IHasExtraProperties
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string?> ExtraProperties { get; set; } = [];
    }

    private sealed class CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "cat_";
        public DbSet<Product> Products => Set<Product>();
    }
}
