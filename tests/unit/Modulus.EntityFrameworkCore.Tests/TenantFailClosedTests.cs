namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Core.Abstractions.Entities;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>
/// Proves the <see cref="ModuleDbContext"/> tenant query filter is
/// <b>fail-closed</b>: an unresolved tenant sees nothing, while the explicit host
/// context sees everything. This is the guard against a missing header or a
/// misconfigured resolver silently returning every tenant's rows.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantFailClosedTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;
    private readonly MutableTenant _tenant = new();

    public TenantFailClosedTests()
    {
        _conn = new SqliteConnection("DataSource=failclosed-mem;Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => _tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped(sp => new ShopDbContext(
            new DbContextOptionsBuilder<ShopDbContext>().UseSqlite(_conn).Options,
            sp.GetRequiredService<ICurrentTenant>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<DomainEventDispatcher>(),
            sp));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task TenantFilter_FailsClosed_ForUnresolvedTenant_AndOpensForHost()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            _tenant.Set(tenantA);
            ctx.Products.Add(new Product { Id = Guid.NewGuid(), Name = "A", TenantId = tenantA });
            await ctx.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();

            // Unresolved tenant (multi-tenancy on, nothing resolved) → fail-closed.
            _tenant.SetUnresolved();
            (await ctx.Products.CountAsync())
                .Should().Be(0, "an unresolved tenant must not see any rows");

            // Wrong tenant → nothing.
            _tenant.Set(tenantB);
            (await ctx.Products.CountAsync())
                .Should().Be(0, "tenant B must not see tenant A's rows");

            // Explicit host scope → sees all.
            _tenant.SetHost();
            (await ctx.Products.CountAsync())
                .Should().Be(1, "the explicit host context sees every tenant's rows");

            // Correct tenant → its own row.
            _tenant.Set(tenantA);
            (await ctx.Products.CountAsync())
                .Should().Be(1, "tenant A sees its own row");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _conn.DisposeAsync();
    }

    // Controllable tenant stub: unlike CurrentTenant it has no AsyncLocal, so the
    // test drives IsHost / TenantId directly and deterministically.
    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => TenantId?.ToString();
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; }

        public void Set(Guid id) { TenantId = id; IsHost = false; }
        public void SetUnresolved() { TenantId = null; IsHost = false; }
        public void SetHost() { TenantId = null; IsHost = true; }

        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class ShopDbContext(
        DbContextOptions<ShopDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "shop_";
        public DbSet<Product> Products => Set<Product>();
    }

    private sealed class Product : IHasTenantId
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid TenantId { get; set; }
    }
}
