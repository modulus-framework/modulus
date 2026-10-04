namespace Modulus.AI.Connector.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ChangeJournalTests : IAsyncLifetime
{
    private readonly JournalTestHost _host = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Inserts_updates_and_deletes_are_journaled_with_the_company_and_key()
    {
        var product = new Product { Name = "Widget" };
        await _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(product);
            db.Notes.Add(new Note { Text = "not indexed" });
            await db.SaveChangesAsync();

            product.Name = "Widget 2";
            await db.SaveChangesAsync();

            db.Products.Remove(product);
            await db.SaveChangesAsync();
        });

        var rows = await _host.JournalAsync();

        rows.Select(r => r.Kind).Should().Equal(AiChangeKind.Upsert, AiChangeKind.Upsert, AiChangeKind.Delete);
        rows.Should().OnlyContain(r => r.ResourceType == "Shop.Product" && r.ResourceId == product.Id.ToString("D")
            && r.TenantId == JournalTestHost.CompanyA.TenantId);
        rows.Select(r => r.Sequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task A_soft_delete_is_journaled_as_a_delete()
    {
        var product = new Product { Name = "Widget" };
        await _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(product);
            await db.SaveChangesAsync();
            product.IsDeleted = true;
            await db.SaveChangesAsync();
        });

        (await _host.JournalAsync()).Select(r => r.Kind).Should().Equal(AiChangeKind.Upsert, AiChangeKind.Delete);
    }

    [Fact]
    public async Task A_save_that_fails_journals_nothing()
    {
        var product = new Product { Name = "Widget" };
        await _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(product);
            await db.SaveChangesAsync();
        });

        var duplicate = () => _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(new Product { Id = product.Id, Name = "Clash" });
            await db.SaveChangesAsync();
        });

        await duplicate.Should().ThrowAsync<DbUpdateException>();
        (await _host.JournalAsync()).Should().ContainSingle("the journal row commits with the entity, or not at all");
    }

    [Fact]
    public async Task A_store_generated_key_fails_the_save()
    {
        var save = () => _host.InAsync(null, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Counters.Add(new Counter { Value = 1 });
            await db.SaveChangesAsync();
        });

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*store-generated key*");
    }

    [Fact]
    public async Task The_feed_merges_contexts_oldest_first_and_resumes_from_its_cursor()
    {
        await AddAsync(JournalTestHost.CompanyA, "P1");
        _host.Time.Advance(TimeSpan.FromSeconds(1));
        await AddOrderAsync("O1");
        _host.Time.Advance(TimeSpan.FromSeconds(1));
        await AddAsync(JournalTestHost.CompanyA, "P2");
        _host.Time.Advance(TimeSpan.FromSeconds(1));
        await AddOrderAsync("O2");
        _host.Time.Advance(TimeSpan.FromMinutes(1));

        var (all, _) = await ReadAllAsync(JournalTestHost.CompanyA.TenantId, max: 100);
        var (paged, pages) = await ReadAllAsync(JournalTestHost.CompanyA.TenantId, max: 1);

        all.Select(c => c.ResourceType).Should().Equal("Shop.Product", "Sales.Order", "Shop.Product", "Sales.Order");
        all.Where(c => c.ResourceType == "Sales.Order").Select(c => c.ResourceId).Should().Equal("O1", "O2");
        paged.Should().Equal(all);
        pages.Should().Be(4);
    }

    [Fact]
    public async Task The_feed_holds_back_rows_that_have_not_settled()
    {
        await AddAsync(JournalTestHost.CompanyA, "Old");
        var settled = _host.Time.Now;
        _host.Time.Advance(TimeSpan.FromSeconds(10));
        await AddAsync(JournalTestHost.CompanyA, "Fresh");

        var page = await _host.InAsync(null, sp => sp.GetRequiredService<EfAiChangeFeed>()
            .ReadAsync(JournalTestHost.CompanyA.TenantId, null, 10, settled));

        page.Changes.Should().ContainSingle();
        page.HasMore.Should().BeFalse("an unsettled row is not ready yet, so the platform need not poll again at once");

        var next = await _host.InAsync(null, sp => sp.GetRequiredService<EfAiChangeFeed>()
            .ReadAsync(JournalTestHost.CompanyA.TenantId, page.Cursor, 10, _host.Time.Now));
        next.Changes.Should().ContainSingle();
        next.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task The_feed_reads_one_company_only()
    {
        await AddAsync(JournalTestHost.CompanyA, "A");
        await AddAsync(JournalTestHost.CompanyB, "B");

        var (a, _) = await ReadAllAsync(JournalTestHost.CompanyA.TenantId, max: 10);
        var (b, _) = await ReadAllAsync(JournalTestHost.CompanyB.TenantId, max: 10);
        var (none, _) = await ReadAllAsync(Guid.NewGuid(), max: 10);

        a.Should().ContainSingle();
        b.Should().ContainSingle();
        a[0].ResourceId.Should().NotBe(b[0].ResourceId);
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task The_head_moves_when_a_change_is_journaled()
    {
        var feed = (Func<Task<string>>)(() => _host.InAsync(null, sp => sp.GetRequiredService<EfAiChangeFeed>().GetHeadAsync(JournalTestHost.CompanyA.TenantId)));

        var empty = await feed();
        await AddAsync(JournalTestHost.CompanyA, "P");
        var first = await feed();
        await AddAsync(JournalTestHost.CompanyB, "Other company");
        var unchanged = await feed();

        EfAiChangeFeed.Decode(empty).Should().BeEmpty();
        first.Should().NotBe(empty);
        unchanged.Should().Be(first);
    }

    [Theory]
    [InlineData("not base64 !")]
    [InlineData("bm90IGpzb24")] // "not json"
    public void A_foreign_cursor_is_refused(string cursor)
        => ((Action)(() => EfAiChangeFeed.Decode(cursor))).Should().Throw<FormatException>();

    [Fact]
    public void Cursors_round_trip()
    {
        var positions = new Dictionary<string, long> { ["ShopDbContext"] = 4, ["SalesDbContext"] = 9 };

        EfAiChangeFeed.Decode(EfAiChangeFeed.Encode(positions)).Should().BeEquivalentTo(positions);
        EfAiChangeFeed.Encode([]).Should().NotBeEmpty("an empty journal still has a position to resume from");
        EfAiChangeFeed.Decode(EfAiChangeFeed.Encode([])).Should().BeEmpty();
        EfAiChangeFeed.Decode(null).Should().BeEmpty();
    }

    [Fact]
    public async Task The_purge_deletes_rows_past_retention_in_every_context()
    {
        await AddAsync(JournalTestHost.CompanyA, "Old");
        await AddOrderAsync("Old order");
        _host.Time.Advance(TimeSpan.FromDays(31));
        await AddAsync(JournalTestHost.CompanyB, "New");

        var deleted = await _host.Purger(TimeSpan.FromDays(30)).PurgeAsync(CancellationToken.None);

        deleted.Should().Be(2);
        (await _host.JournalAsync()).Should().ContainSingle(r => r.TenantId == JournalTestHost.CompanyB.TenantId);
    }

    private Task AddAsync(TenantInfo tenant, string name)
        => _host.InAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<ShopDbContext>();
            db.Products.Add(new Product { Name = name });
            await db.SaveChangesAsync();
        });

    private Task AddOrderAsync(string id)
        => _host.InAsync(JournalTestHost.CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<SalesDbContext>();
            db.Orders.Add(new Order { Id = id, Total = 1 });
            await db.SaveChangesAsync();
        });

    private async Task<(List<AiJournalChange> Changes, int Pages)> ReadAllAsync(Guid tenant, int max)
    {
        var changes = new List<AiJournalChange>();
        string? cursor = null;
        var pages = 0;
        while (pages < 50)
        {
            var page = await _host.InAsync(null, sp => sp.GetRequiredService<EfAiChangeFeed>().ReadAsync(tenant, cursor, max, _host.Time.Now));
            if (page.Changes.Count == 0)
                break;
            changes.AddRange(page.Changes);
            pages++;
            cursor = page.Cursor;
            if (!page.HasMore)
                break;
        }

        return (changes, pages);
    }
}
