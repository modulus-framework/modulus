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
/// Proves the <see cref="ModuleDbContext"/> organizational-scope query filter is
/// <b>fail-closed</b> (a principal restricted to no units sees nothing; an
/// unrestricted principal sees everything), that list and single-item reads apply
/// the <i>same</i> predicate, and that org scope AND-composes with tenant isolation
/// and soft-delete rather than replacing them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrgScopeFailClosedTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;
    private readonly MutableTenant _tenant = new();
    private readonly MutableDataScope _scope = new();

    private static readonly Guid UnitX = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UnitY = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public OrgScopeFailClosedTests()
    {
        _conn = new SqliteConnection("DataSource=orgscope-mem;Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => _tenant);
        services.AddScoped<ICurrentDataScope>(_ => _scope);
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
    public async Task OrgScope_FailsClosed_ForRestricted_AndOpensForUnrestricted()
    {
        var xId = Guid.NewGuid();
        var yId = Guid.NewGuid();

        // Seed in an unrestricted, host scope so inserts aren't themselves filtered.
        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            await ctx.Database.EnsureCreatedAsync();
            _tenant.SetHost();
            _scope.Unrestricted();

            ctx.Docs.Add(new Doc { Id = xId, Name = "X", OrgUnitId = UnitX });
            ctx.Docs.Add(new Doc { Id = yId, Name = "Y", OrgUnitId = UnitY });
            await ctx.SaveChangesAsync();
        }

        // Fresh scope (new DbContext instance) — proves the filter re-roots to the
        // executing context rather than freezing the seed scope into the model.
        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            _tenant.SetHost();

            // Restricted to UnitX → sees only the UnitX row (list AND single).
            _scope.Restrict(UnitX);
            (await ctx.Docs.CountAsync())
                .Should().Be(1, "only the UnitX document is in scope");
            (await ctx.Docs.FirstOrDefaultAsync(d => d.Id == yId))
                .Should().BeNull("the single-item read applies the same org filter as the list");
            (await ctx.Docs.FirstOrDefaultAsync(d => d.Id == xId))
                .Should().NotBeNull("the in-scope document is reachable by id");

            // No units, not unrestricted → fail-closed, nothing.
            _scope.None();
            (await ctx.Docs.CountAsync())
                .Should().Be(0, "a principal scoped to no unit must not see any rows");

            // Explicit unrestricted → sees all.
            _scope.Unrestricted();
            (await ctx.Docs.CountAsync())
                .Should().Be(2, "an unrestricted principal sees every unit's rows");
        }
    }

    [Fact]
    public async Task OrgScope_ComposesWith_Tenant_AndSoftDelete()
    {
        var tenantA = Guid.NewGuid();
        var visibleId = Guid.NewGuid();

        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            await ctx.Database.EnsureCreatedAsync();
            _tenant.SetHost();
            _scope.Unrestricted();

            // In tenant A / UnitX, not deleted → the one row that satisfies all layers.
            ctx.Files.Add(new FullFile { Id = visibleId, TenantId = tenantA, OrgUnitId = UnitX });
            // Right org unit, wrong tenant.
            ctx.Files.Add(new FullFile { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), OrgUnitId = UnitX });
            // Right tenant, wrong org unit.
            ctx.Files.Add(new FullFile { Id = Guid.NewGuid(), TenantId = tenantA, OrgUnitId = UnitY });
            // Right tenant + org unit, but soft-deleted.
            ctx.Files.Add(new FullFile { Id = Guid.NewGuid(), TenantId = tenantA, OrgUnitId = UnitX, IsDeleted = true });
            await ctx.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            _tenant.Set(tenantA);
            _scope.Restrict(UnitX);

            var visible = await ctx.Files.ToListAsync();

            visible.Should().ContainSingle(f => f.Id == visibleId,
                "only the row matching tenant AND org scope AND not-deleted survives all three filters");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => TenantId?.ToString();
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; }

        public void Set(Guid id) { TenantId = id; IsHost = false; }
        public void SetHost() { TenantId = null; IsHost = true; }

        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class MutableDataScope : ICurrentDataScope
    {
        private Guid[] _units = [];
        public bool IsUnrestricted { get; private set; }
        public IReadOnlyCollection<Guid> OrgUnitIds => _units;

        public void Restrict(params Guid[] units) { _units = units; IsUnrestricted = false; }
        public void None() { _units = []; IsUnrestricted = false; }
        public void Unrestricted() { _units = []; IsUnrestricted = true; }
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
        public DbSet<Doc> Docs => Set<Doc>();
        public DbSet<FullFile> Files => Set<FullFile>();
    }

    private sealed class Doc : IHasOrgUnit
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid OrgUnitId { get; set; }
    }

    private sealed class FullFile : IHasTenantId, IHasOrgUnit, ISoftDelete
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid OrgUnitId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
