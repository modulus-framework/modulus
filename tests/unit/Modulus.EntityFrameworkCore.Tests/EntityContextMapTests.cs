namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>
/// The registration-time entity→context map routes each entity to exactly the
/// module <see cref="DbContext"/> that owns it. The critical property (vs. the
/// old per-resolution model scan) is that resolving a repository instantiates
/// <b>only</b> the owning context — not every registered context.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EntityContextMapTests : IAsyncDisposable
{
    private readonly SqliteConnection _catalogConn;
    private readonly SqliteConnection _ordersConn;
    private readonly ServiceProvider _root;

    public EntityContextMapTests()
    {
        _catalogConn = new SqliteConnection("DataSource=map-catalog;Mode=Memory;Cache=Shared");
        _catalogConn.Open();
        _ordersConn = new SqliteConnection("DataSource=map-orders;Mode=Memory;Cache=Shared");
        _ordersConn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<CatalogCtx>(o => o.UseSqlite(_catalogConn));
        services.AddModuleDatabase<OrdersCtx>(o => o.UseSqlite(_ordersConn));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public void Map_RoutesEachEntity_ToItsOwningContextType()
    {
        var map = _root.GetRequiredService<IEntityContextMap>();

        map.Resolve(typeof(CatalogItem)).Should().Be<CatalogCtx>();
        map.Resolve(typeof(OrderTicket)).Should().Be<OrdersCtx>();
    }

    [Fact]
    public void Map_UnknownEntity_ResolvesToNull()
        => _root.GetRequiredService<IEntityContextMap>()
            .Resolve(typeof(string)).Should().BeNull();

    [Fact]
    public async Task ResolvingRepository_InstantiatesOnlyTheOwningContext()
    {
        // Force the singleton map to build once (it resolves every context in a
        // throwaway scope to read metadata). Reset counters afterwards so the
        // assertion measures only the request scope below.
        _ = _root.GetRequiredService<IEntityContextMap>().Resolve(typeof(CatalogItem));
        CatalogCtx.Constructions = 0;
        OrdersCtx.Constructions = 0;

        using (var scope = _root.CreateScope())
        {
            // Touch the Catalog repository — .AddAsync resolves its DbContext.
            await scope.ServiceProvider.GetRequiredService<IRepository<CatalogItem>>()
                .AddAsync(new CatalogItem { Name = "Widget" }, default);
        }

        CatalogCtx.Constructions.Should().Be(1, "the owning context is resolved");
        OrdersCtx.Constructions.Should().Be(0,
            "the unrelated module context must NOT be instantiated to route an entity");
    }

    // ── Fixtures ──────────────────────────────────────────────────
    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class CatalogCtx(
        DbContextOptions<CatalogCtx> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        public static int Constructions;
        // Field initializer runs on every construction — counts instantiation
        // deterministically (unlike OnConfiguring, which fires lazily on use).
        private readonly bool _tracked = Count();
        private static bool Count() { Constructions++; return true; }
        protected override string TablePrefix => "cat_";
        public DbSet<CatalogItem> Items => Set<CatalogItem>();
    }

    private sealed class OrdersCtx(
        DbContextOptions<OrdersCtx> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        public static int Constructions;
        private readonly bool _tracked = Count();
        private static bool Count() { Constructions++; return true; }
        protected override string TablePrefix => "ord_";
        public DbSet<OrderTicket> Tickets => Set<OrderTicket>();
    }

    private sealed class CatalogItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OrderTicket
    {
        public Guid Id { get; set; }
        public decimal Total { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _ordersConn.DisposeAsync();
        await _catalogConn.DisposeAsync();
    }
}
