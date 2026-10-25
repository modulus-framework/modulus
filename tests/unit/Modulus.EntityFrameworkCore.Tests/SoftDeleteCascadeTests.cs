namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.Events;
using Xunit;

/// <summary>Soft-deleting a parent keeps its children: they are not hard-deleted by the cascade the removal set off.</summary>
[Trait("Category", "Unit")]
public sealed class SoftDeleteCascadeTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;

    public SoftDeleteCascadeTests()
    {
        _conn = new SqliteConnection($"DataSource=softcascade-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => new NullCurrentTenant());
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped(sp => new MemosDbContext(
            new DbContextOptionsBuilder<MemosDbContext>().UseSqlite(_conn).Options,
            sp.GetRequiredService<ICurrentTenant>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<DomainEventDispatcher>(),
            sp));
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<MemosDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Soft_deleting_a_parent_keeps_its_children()
    {
        var parentId = Guid.NewGuid();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemosDbContext>();
            db.Memos.Add(new Memo { Id = parentId });
            db.Lines.Add(new MemoLine { Id = Guid.NewGuid(), MemoId = parentId, Text = "kept" });
            await db.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemosDbContext>();
            var parent = await db.Memos.Include(m => m.Lines).SingleAsync(m => m.Id == parentId);
            db.Memos.Remove(parent);
            await db.SaveChangesAsync();
        }

        using var check = _root.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<MemosDbContext>();
        (await verify.Memos.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == parentId)).IsDeleted.Should().BeTrue();
        (await verify.Lines.AsNoTracking().CountAsync()).Should().Be(1, "the children of a soft-deleted parent are not destroyed");
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private sealed class MemosDbContext(
        DbContextOptions<MemosDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "cascade_";
        public DbSet<Memo> Memos => Set<Memo>();
        public DbSet<MemoLine> Lines => Set<MemoLine>();
    }

    private sealed class Memo : ISoftDelete
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public List<MemoLine> Lines { get; } = [];
        public string? DeletedBy { get; set; }
    }

    private sealed class MemoLine
    {
        public Guid Id { get; set; }
        public Guid MemoId { get; set; }
        public Memo? Memo { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
