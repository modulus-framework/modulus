using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Media;
using Modulus.Media.EntityFrameworkCore;
using Xunit;

namespace Modulus.Media.EntityFrameworkCore.Tests;

/// <summary>The EF media store: rows round-trip, companies are isolated (the host included), and registration replaces the in-memory store.</summary>
[Trait("Category", "Unit")]
public sealed class EfMediaStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<TestMediaContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestMediaContext>().UseSqlite(_connection).Options;
        await using var db = new TestMediaContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_row_round_trips_with_every_field()
    {
        var store = Store();
        var item = Item(Guid.NewGuid(), "contract.pdf", MediaKind.Document);

        await store.AddAsync(item);

        (await store.GetAsync(item.TenantId, item.Id)).Should().Be(item);
    }

    [Fact]
    public async Task Companies_are_isolated_and_the_host_is_its_own_company()
    {
        var store = Store();
        var companyA = Guid.NewGuid();
        var a = Item(companyA, "a.pdf", MediaKind.Document);
        var host = Item(null, "host.pdf", MediaKind.Document);
        await store.AddAsync(a);
        await store.AddAsync(host);

        (await store.GetAsync(Guid.NewGuid(), a.Id)).Should().BeNull();
        (await store.GetAsync(null, a.Id)).Should().BeNull();
        (await store.QueryAsync(null, new MediaQuery())).Total.Should().Be(1);
        (await store.QueryAsync(companyA, new MediaQuery())).Total.Should().Be(1);
        (await store.RemoveAsync(Guid.NewGuid(), a.Id)).Should().BeFalse();
        (await store.GetAsync(companyA, a.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Query_filters_by_kind_and_search_and_pages_with_a_total()
    {
        var store = Store();
        var company = Guid.NewGuid();
        await store.AddAsync(Item(company, "Invoice-1.png", MediaKind.Image));
        await store.AddAsync(Item(company, "Invoice-2.pdf", MediaKind.Document));
        await store.AddAsync(Item(company, "logo.png", MediaKind.Image));

        (await store.QueryAsync(company, new MediaQuery(Kind: MediaKind.Image))).Total.Should().Be(2);
        (await store.QueryAsync(company, new MediaQuery(Search: "invoice"))).Total.Should().Be(2);

        var page = await store.QueryAsync(company, new MediaQuery(PageSize: 2, Page: 2));
        page.Total.Should().Be(3);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Remove_deletes_the_row_of_its_own_company_only()
    {
        var store = Store();
        var company = Guid.NewGuid();
        var item = Item(company, "gone.pdf", MediaKind.Document);
        await store.AddAsync(item);

        (await store.RemoveAsync(company, item.Id)).Should().BeTrue();
        (await store.GetAsync(company, item.Id)).Should().BeNull();
    }

    [Fact]
    public void Registration_replaces_the_in_memory_store_whichever_order_it_is_added_in()
    {
        var services = new ServiceCollection();
        services.AddModulusMedia();
        services.AddDbContext<TestMediaContext>(o => o.UseSqlite(_connection));
        services.AddModulusMediaStore<TestMediaContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMediaStore>().Should().BeOfType<EfMediaStore>();
    }

    private EfMediaStore Store()
    {
        var db = new TestMediaContext(_options);
        return new EfMediaStore(db);
    }

    private static MediaItem Item(Guid? tenant, string name, MediaKind kind) => new(
        Guid.NewGuid(),
        tenant,
        name,
        kind == MediaKind.Image ? "image/png" : "application/pdf",
        kind,
        1234,
        new string('a', 64),
        $"media/x/{Guid.NewGuid():N}.{(kind == MediaKind.Image ? "png" : "pdf")}",
        null,
        new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(0, 1000)));

    private sealed class TestMediaContext(DbContextOptions<TestMediaContext> options) : ModulusMediaDbContext(options);
}
