#if NET10_0_OR_GREATER
namespace Modulus.EFCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Data.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>
/// Keyset paging on a Guid v7 key against PostgreSQL. PostgreSQL orders <c>uuid</c> bytewise in RFC order, so the pages
/// must follow creation time, which is the time prefix of the Guid v7 key.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GuidV7KeysetPostgresTests(PostgreSqlFixture fixture)
    : EFCoreIntegrationTestBase(fixture), IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Guid_v7_keyset_pages_walk_the_whole_table_in_uuid_order()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(ConnectionString);
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var product = new TestProduct(Guid.CreateVersion7()) { Name = $"Product {i}" };
            ids.Add(product.Id);
            db.Products.Add(product);
        }
        await db.SaveChangesAsync();

        // The RFC byte order of a Guid is the order of its lowercase text form.
        var expected = ids.OrderBy(id => id.ToString("D")).ToList();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<TestProduct>>();

        var collected = new List<Guid>();
        Guid? after = null;
        KeysetPage<Guid> page;
        do
        {
            page = await repo.ListKeysetAsync(new ProductsIn(ids), p => p.Id, p => p.Id, after, 10, default);
            collected.AddRange(page.Items);
            after = page.Items.Count > 0 ? page.Items[^1] : null;
        }
        while (page.HasNextPage);

        collected.Should().Equal(expected);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task A_guid_cursor_decodes_to_the_last_id_on_the_page()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(ConnectionString);
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();
        for (var i = 0; i < 6; i++)
            db.Products.Add(new TestProduct(Guid.CreateVersion7()) { Name = $"Cursor {i}" });
        await db.SaveChangesAsync();

        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<TestProduct>>();
        var page = await repo.ListKeysetAsync(new AllProducts(), p => p.Id, p => p.Id, null, 3, default);

        page.HasNextPage.Should().BeTrue();
        CursorCodec.Decode<Guid>(page.NextCursor!).Should().Be(page.Items[^1]);
    }

    [Fact]
    public async Task A_key_the_application_does_not_set_is_generated_as_guid_v7_on_postgres()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(ConnectionString);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();
        var product = new TestProduct { Name = "Generated key" };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // The version digit is the first character of the third group of the "D" form.
        product.Id.ToString("D")[14].Should().Be('7');
    }

    private sealed class AllProducts : Specification<TestProduct>
    {
        public AllProducts() => Filter = _ => true;
    }

    // The tests share one database, so the first test pages only through the rows it created.
    private sealed class ProductsIn : Specification<TestProduct>
    {
        public ProductsIn(IReadOnlyCollection<Guid> ids) => Filter = p => ids.Contains(p.Id);
    }
}
#endif
