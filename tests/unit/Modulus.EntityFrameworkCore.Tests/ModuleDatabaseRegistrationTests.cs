namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public class ModuleDatabaseRegistrationTests
{
    /// <summary>
    /// <see cref="EFCoreServiceCollectionExtensions.AddModuleDatabase{TContext}"/>
    /// must expose every module context as <see cref="DbContext"/> so that
    /// <c>TransactionBehavior</c> discovers all of them and
    /// <see cref="EfRepository{T}"/> can route each entity to its owning context.
    /// </summary>
    [Fact]
    public async Task AddModuleDatabase_RegistersEveryContextAsDbContext_AndRoutesEntities()
    {
        await using var fixture = new TwoModuleFixture();
        await fixture.EnsureSchemasAsync();

        using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        // Every module context is discoverable as DbContext (TransactionBehavior).
        var contexts = sp.GetServices<DbContext>().ToList();
        contexts.Should().HaveCount(2);
        contexts.Should().ContainSingle(c => c is CatalogTestDbContext);
        contexts.Should().ContainSingle(c => c is OrdersTestDbContext);

        // EfRepository<T> routes to the context whose model owns entity T.
        await sp.GetRequiredService<IRepository<CatalogItem>>()
            .AddAsync(new CatalogItem { Name = "Widget" }, default);
        await sp.GetRequiredService<IRepository<OrderTicket>>()
            .AddAsync(new OrderTicket { Total = 9.99m }, default);

        await sp.GetRequiredService<CatalogTestDbContext>().SaveChangesAsync();
        await sp.GetRequiredService<OrdersTestDbContext>().SaveChangesAsync();

        (await sp.GetRequiredService<CatalogTestDbContext>().Items.CountAsync())
            .Should().Be(1, "CatalogItem was routed to CatalogTestDbContext");
        (await sp.GetRequiredService<OrdersTestDbContext>().Tickets.CountAsync())
            .Should().Be(1, "OrderTicket was routed to OrdersTestDbContext");
    }

    /// <summary>
    /// <see cref="EFCoreServiceCollectionExtensions.AddModuleDatabase{TContext}"/>
    /// must NOT register <see cref="IUnitOfWork"/>. In a modular monolith each
    /// module owns and registers its own unit-of-work abstraction; auto-registering
    /// a shared one caused a last-wins race that silently dropped commits for all
    /// but the last-registered module.
    /// </summary>
    [Fact]
    public async Task AddModuleDatabase_DoesNotRegisterUnitOfWork()
    {
        await using var fixture = new TwoModuleFixture();
        using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetService<IUnitOfWork>()
            .Should().BeNull("the module must register its own IUnitOfWork");
    }

    // ── Fixture: each module gets its own in-memory SQLite database so the two
    //    contexts never share schema/tables (mirrors one-DbContext-per-module). ──
    private sealed class TwoModuleFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _catalogConn;
        private readonly SqliteConnection _ordersConn;
        private readonly ServiceProvider _root;

        public TwoModuleFixture()
        {
            _catalogConn = new SqliteConnection("DataSource=catalog-mem;Mode=Memory;Cache=Shared");
            _catalogConn.Open();
            _ordersConn = new SqliteConnection("DataSource=orders-mem;Mode=Memory;Cache=Shared");
            _ordersConn.Open();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ICurrentTenant, NullTenant>();
            services.AddScoped<ICurrentUser, NullCurrentUser>();
            services.AddScoped<DomainEventDispatcher>();
            services.AddModuleDatabase<CatalogTestDbContext>(o => o.UseSqlite(_catalogConn));
            services.AddModuleDatabase<OrdersTestDbContext>(o => o.UseSqlite(_ordersConn));
            _root = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => _root.CreateScope();

        public async Task EnsureSchemasAsync()
        {
            using var scope = _root.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<CatalogTestDbContext>()
                .Database.EnsureCreatedAsync();
            await scope.ServiceProvider
                .GetRequiredService<OrdersTestDbContext>()
                .Database.EnsureCreatedAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _root.DisposeAsync();
            await _ordersConn.DisposeAsync();
            await _catalogConn.DisposeAsync();
        }
    }

    /// <summary>
    /// Minimal <see cref="ICurrentTenant"/> that reports no tenant (host/no-tenant
    /// context) so the query filters degrade to match-all.
    /// </summary>
    private sealed class NullTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        // Host context: query filters degrade to match-all (multi-tenancy off).
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class CatalogTestDbContext(
        DbContextOptions<CatalogTestDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "cat_";
        public DbSet<CatalogItem> Items => Set<CatalogItem>();
    }

    private sealed class OrdersTestDbContext(
        DbContextOptions<OrdersTestDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
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
}
