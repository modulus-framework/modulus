namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>
/// The write-side twin of the tenant query filter: outside the host context a unit of work may
/// only insert, move, update or delete rows of the ambient tenant.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CrossTenantWriteGuardTests : IAsyncDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;
    private readonly MutableTenant _tenant = new();

    public CrossTenantWriteGuardTests()
    {
        _conn = new SqliteConnection($"DataSource=xtenant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => _tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped(sp => new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseSqlite(_conn).Options,
            sp.GetRequiredService<ICurrentTenant>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<DomainEventDispatcher>(),
            sp));
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Insert_StampedWithForeignTenant_Throws()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        ctx.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = TenantB });

        var act = () => ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<CrossTenantWriteException>())
            .Which.EntityTenantId.Should().Be(TenantB);
        _tenant.SetHost();
        (await ctx.Notes.IgnoreQueryFilters().CountAsync()).Should().Be(0, "nothing reached the database");
    }

    [Fact]
    public async Task Insert_WithoutTenant_IsStamped_AndOwnTenantIsAllowed()
    {
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var stamped = new Note { Id = Guid.NewGuid() };
        ctx.Notes.Add(stamped);
        ctx.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = TenantA });

        await ctx.SaveChangesAsync();

        stamped.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public async Task MovingRowToAnotherTenant_Throws()
    {
        var id = await SeedAsync(TenantA);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var note = await ctx.Notes.SingleAsync(n => n.Id == id);
        note.TenantId = TenantB;

        var act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task AttachedForeignRow_UpdateOrDelete_Throws(EntityState state)
    {
        var id = await SeedAsync(TenantB);
        _tenant.Set(TenantA);
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        ctx.Entry(new Note { Id = id, TenantId = TenantB, Text = "hijack" }).State = state;

        var act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task NoTenantResolved_ForeignStampedInsert_Throws()
    {
        _tenant.SetNone();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        ctx.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = TenantA });

        var act = () => ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<CrossTenantWriteException>())
            .Which.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public async Task HostContext_MayWriteAnyTenant()
    {
        var id = await SeedAsync(TenantA);
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var note = await ctx.Notes.SingleAsync(n => n.Id == id);
        note.TenantId = TenantB;
        ctx.Notes.Add(new Note { Id = Guid.NewGuid(), TenantId = TenantB });

        await ctx.SaveChangesAsync();

        (await ctx.Notes.CountAsync(n => n.TenantId == TenantB)).Should().Be(2);
    }

    private async Task<Guid> SeedAsync(Guid tenantId)
    {
        _tenant.SetHost();
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var id = Guid.NewGuid();
        ctx.Notes.Add(new Note { Id = id, TenantId = tenantId });
        await ctx.SaveChangesAsync();
        return id;
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
        public void SetNone() { TenantId = null; IsHost = false; }

        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class NotesDbContext(
        DbContextOptions<NotesDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "notes_";
        public DbSet<Note> Notes => Set<Note>();
    }

    private sealed class Note : IHasTenantId
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
