namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events;
using Xunit;

/// <summary>
/// <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> skip the change tracker, so the save-time write guard and the soft delete
/// never see them. The command interceptor checks the statement instead.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BulkOperationGuardTests : IAsyncDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;
    private readonly MutableTenant _tenant = new();

    public BulkOperationGuardTests()
    {
        _conn = new SqliteConnection($"DataSource=bulk-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => _tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped(sp => new MemosDbContext(
            new DbContextOptionsBuilder<MemosDbContext>()
                .UseSqlite(_conn)
                .AddInterceptors(TenantSqlGuardInterceptor.Instance)
                .Options,
            sp.GetRequiredService<ICurrentTenant>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<DomainEventDispatcher>(),
            sp));
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<MemosDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task ExecuteUpdate_MovingRowsToAnotherTenant_IsRefusedInsideATenant()
    {
        var id = await SeedAsync(TenantA);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();

        var act = () => ctx.Memos.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TenantId, TenantB));

        await act.Should().ThrowAsync<CrossTenantSqlException>();
        (await StoredAsync(id)).TenantId.Should().Be(TenantA, "the row stayed with its company");
    }

    [Fact]
    public async Task ExecuteUpdate_SettingOtherColumns_IsAllowed()
    {
        var id = await SeedAsync(TenantA);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();

        var updated = await ctx.Memos.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Text, "edited"));

        updated.Should().Be(1);
        (await StoredAsync(id)).Text.Should().Be("edited");
    }

    [Fact]
    public async Task ExecuteUpdate_MovingRowsToAnotherTenant_IsAllowedInsideAReviewedScope()
    {
        var id = await SeedAsync(TenantA);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();

        using (CrossTenantSql.Allow("Company split: the memo is re-homed to its new company"))
        {
            await ctx.Memos.Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.TenantId, TenantB));
        }

        (await StoredAsync(id)).TenantId.Should().Be(TenantB);
    }

    [Fact]
    public async Task ExecuteDelete_OnASoftDeleteEntity_IsRefusedWithoutAHardDeleteScope()
    {
        var id = await SeedAsync(TenantA);
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();

        var act = () => ctx.Memos.IgnoreQueryFilters().Where(m => m.Id == id).ExecuteDeleteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await StoredAsync(id)).Should().NotBeNull("the row was not destroyed");
    }

    [Fact]
    public async Task ExecuteDelete_OnASoftDeleteEntity_IsAllowedInsideHardDeleteScope()
    {
        var id = await SeedAsync(TenantA);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();

        using (HardDelete.Allow("Right to erasure request 4711"))
        {
            (await ctx.Memos.Where(m => m.Id == id).ExecuteDeleteAsync()).Should().Be(1);
        }
    }

    private async Task<Guid> SeedAsync(Guid tenantId)
    {
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();
        var id = Guid.NewGuid();
        ctx.Memos.Add(new Memo { Id = id, TenantId = tenantId });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task<Memo> StoredAsync(Guid id)
    {
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MemosDbContext>();
        return await ctx.Memos.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == id);
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

    private sealed class MemosDbContext(
        DbContextOptions<MemosDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "memos_";
        public DbSet<Memo> Memos => Set<Memo>();
    }

    private sealed class Memo : IHasTenantId, ISoftDelete
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
