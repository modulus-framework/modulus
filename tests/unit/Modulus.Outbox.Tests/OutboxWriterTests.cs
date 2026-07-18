using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Outbox;
using Modulus.Outbox.Abstractions;
using NSubstitute;
using Xunit;

namespace Modulus.Outbox.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxWriterTests
{
    // ── EfOutboxWriter: transactional persistence ──────────────────

    [Fact]
    public async Task Enqueue_ThenSaveChanges_PersistsOutboxRow()
    {
        await using var h = await WriterHarness.BuildAsync();
        var outbox = h.Scope.ServiceProvider.GetRequiredService<IIntegrationEventOutbox>();

        outbox.Enqueue(new TestIntegrationEvent("payload-A"));
        await h.Db.SaveChangesAsync();

        var rows = await h.ReadOutboxRowsAsync();
        rows.Should().HaveCount(1);
        rows[0].MessageType.Should().Contain(nameof(TestIntegrationEvent));
        rows[0].Payload.Should().Contain("payload-A");
        rows[0].ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_ThenSaveChanges_InTransaction_ThenRollback_RowGone()
    {
        await using var h = await WriterHarness.BuildAsync();
        var outbox = h.Scope.ServiceProvider.GetRequiredService<IIntegrationEventOutbox>();

        await using var tx = await h.Db.Database.BeginTransactionAsync();
        outbox.Enqueue(new TestIntegrationEvent("payload-B"));
        await h.Db.SaveChangesAsync();

        // Row is visible inside the transaction
        (await h.CountOutboxRowsAsync()).Should().Be(1);

        await tx.RollbackAsync();

        // Row is gone after rollback — proving the outbox write was
        // in the same transaction as SaveChanges (no dual-write gap).
        (await h.CountOutboxRowsAsync()).Should().Be(0);
    }

    // ── Null outbox ────────────────────────────────────────────────

    [Fact]
    public void NullOutbox_Enqueue_IsNoOp()
    {
        var outbox = new NullIntegrationEventOutbox();
        var act = () => outbox.Enqueue(new TestIntegrationEvent("x"));
        act.Should().NotThrow();
    }

    // ── ModuleDbContext integration: domain events → outbox ────────

    [Fact]
    public async Task ModuleDbContext_SaveChanges_EnqueuesIntegrationEventsBeforeSave()
    {
        await using var h = await ModuleHarness.BuildAsync();

        // Add an aggregate that raises a domain event which ALSO implements
        // IIntegrationEvent — ModuleDbContext should add an outbox row to
        // THIS context's change tracker BEFORE calling base.SaveChangesAsync.
        h.Db.TestItems.Add(new TestItem(Guid.NewGuid(), "widget"));
        await h.Db.SaveChangesAsync();

        (await h.CountOutboxRowsAsync()).Should().Be(1);
        var rows = await h.ReadOutboxRowsAsync();
        rows[0].MessageType.Should().Contain(nameof(TestItemCreatedEvent));
    }

    [Fact]
    public async Task ModuleDbContext_SaveChanges_DoesNotEnqueuePlainDomainEvents()
    {
        await using var h = await ModuleHarness.BuildAsync();

        h.Db.TestItemsWithPlainEvent.Add(new TestItemWithPlainEvent(Guid.NewGuid(), "gadget"));
        await h.Db.SaveChangesAsync();

        (await h.CountOutboxRowsAsync()).Should().Be(0,
            "the domain event does not implement IIntegrationEvent");
    }

    [Fact]
    public async Task ModuleDbContext_WithoutOutbox_SaveChangesStillWorks()
    {
        // When no IIntegrationEventOutbox is registered, ModuleDbContext
        // must still save successfully (sp.GetService returns null → skip).
        await using var h = await ModuleHarness.BuildAsync(registerOutbox: false);

        h.Db.TestItems.Add(new TestItem(Guid.NewGuid(), "no-outbox"));
        var rows = await h.Db.SaveChangesAsync();

        rows.Should().BeGreaterThan(0);
        (await h.CountOutboxRowsAsync()).Should().Be(0);
    }

    // ── Harness: EfOutboxWriter with real SQLite ───────────────────

    private sealed class WriterHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly ServiceProvider _root;

        private WriterHarness(SqliteConnection conn, ServiceProvider root, IServiceScope scope, OutboxDbContext db)
        {
            _conn = conn;
            _root = root;
            Scope = scope;
            Db = db;
        }

        public IServiceScope Scope { get; }
        public OutboxDbContext Db { get; }

        public static async Task<WriterHarness> BuildAsync()
        {
            var conn = new SqliteConnection("DataSource=:memory:");
            await conn.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(conn));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<OutboxDbContext>());

            // EfOutboxWriter dependencies
            var tenant = Substitute.For<ICurrentTenant>();
            tenant.TenantId.Returns((Guid?)null);
            services.AddSingleton(tenant);
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IIntegrationEventOutbox, EfOutboxWriter>();

            var root = services.BuildServiceProvider();
            var scope = root.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await db.Database.EnsureCreatedAsync();

            return new WriterHarness(conn, root, scope, db);
        }

        public Task<List<OutboxMessage>> ReadOutboxRowsAsync() =>
            Db.Set<OutboxMessage>().AsNoTracking().ToListAsync();

        public Task<int> CountOutboxRowsAsync() =>
            Db.Set<OutboxMessage>().CountAsync();

        public async ValueTask DisposeAsync()
        {
            await _root.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    // ── Harness: ModuleDbContext with spy outbox ───────────────────

    private sealed class ModuleHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly ServiceProvider _root;

        private ModuleHarness(SqliteConnection conn, ServiceProvider root, IServiceScope scope, TestModuleDbContext db)
        {
            _conn = conn;
            _root = root;
            Scope = scope;
            Db = db;
        }

        public IServiceScope Scope { get; }
        public TestModuleDbContext Db { get; }

        public static async Task<ModuleHarness> BuildAsync(bool registerOutbox = true)
        {
            var conn = new SqliteConnection("DataSource=:memory:");
            await conn.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<TestModuleDbContext>(o => o.UseSqlite(conn));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestModuleDbContext>());

            services.AddSingleton<ICurrentTenant>(Substitute.For<ICurrentTenant>());
            services.AddSingleton<ICurrentUser>(Substitute.For<ICurrentUser>());
            services.AddScoped<DomainEventDispatcher>();

            if (registerOutbox)
                services.AddScoped<IOutboxWriter, StubOutboxWriter>();

            var root = services.BuildServiceProvider();
            var scope = root.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            await db.Database.EnsureCreatedAsync();

            return new ModuleHarness(conn, root, scope, db);
        }

        public Task<int> CountOutboxRowsAsync() =>
            Db.Set<OutboxMessage>().AsNoTracking().CountAsync();

        public async Task<List<OutboxMessage>> ReadOutboxRowsAsync() =>
            await Db.Set<OutboxMessage>().AsNoTracking().ToListAsync();

        public async ValueTask DisposeAsync()
        {
            await _root.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    /// <summary>
    /// Minimal IOutboxWriter stub that signals the outbox is enabled.
    /// ModuleDbContext adds rows directly to its own change tracker —
    /// this stub just satisfies the IOutboxWriter registration check.
    /// </summary>
    internal sealed class StubOutboxWriter : IOutboxWriter
    {
        public Task WriteAsync<TEvent>(
            TEvent @event, CancellationToken ct = default)
            where TEvent : IIntegrationEvent
            => Task.CompletedTask;
    }

    // ── Test doubles ───────────────────────────────────────────────

    internal sealed class OutboxDbContext(
        DbContextOptions<OutboxDbContext> opts) : DbContext(opts)
    {
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<OutboxMessage>(b =>
            {
                b.ToTable("outbox_messages");
                b.HasKey(x => x.Id);
                b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
                b.Property(x => x.Payload).IsRequired();
                b.Property(x => x.ModuleName).HasMaxLength(100);
                b.HasIndex(x => new { x.ProcessedAt, x.LockedUntil, x.RetryCount });
                b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
                b.HasIndex(x => x.TenantId);
            });
        }
    }

    internal sealed class TestModuleDbContext(
        DbContextOptions<TestModuleDbContext> opts,
        ICurrentTenant tenant,
        ICurrentUser user,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(opts, tenant, user, dispatcher, sp)
    {
        protected override string TablePrefix => "ot_";
        public DbSet<TestItem> TestItems => Set<TestItem>();
        public DbSet<TestItemWithPlainEvent> TestItemsWithPlainEvent => Set<TestItemWithPlainEvent>();
    }

    internal sealed class TestItem : AggregateRoot
    {
        public TestItem(Guid id, string name)
        {
            Id = id;
            Name = name;
            AddDomainEvent(new TestItemCreatedEvent(id, name));
        }

        // EF Core parameterless constructor for materialization.
        public TestItem() { Id = Guid.NewGuid(); Name = ""; }

        public string Name { get; set; } = default!;
    }

    internal sealed class TestItemWithPlainEvent : AggregateRoot
    {
        public TestItemWithPlainEvent(Guid id, string name)
        {
            Id = id;
            Name = name;
            AddDomainEvent(new PlainDomainEvent());
        }

        public TestItemWithPlainEvent() { Id = Guid.NewGuid(); Name = ""; }

        public string Name { get; set; } = default!;
    }

    /// <summary>Domain event that ALSO implements IIntegrationEvent — should be enqueued to the outbox.</summary>
    public sealed record TestItemCreatedEvent(Guid ItemId, string Name)
        : DomainEventBase, IIntegrationEvent
    {
        public string EventType => "test.item-created.v1";
    }

    /// <summary>Domain event that does NOT implement IIntegrationEvent — should NOT be enqueued.</summary>
    public sealed record PlainDomainEvent : DomainEventBase;

    /// <summary>Standalone integration event for writer tests.</summary>
    public sealed record TestIntegrationEvent(string Payload)
        : DomainEventBase, IIntegrationEvent
    {
        public string EventType => "test.integration.v1";
    }

    /// <summary>Captures enqueued events without touching a DbContext.</summary>
    internal sealed class SpyOutbox : IIntegrationEventOutbox
    {
        public List<IIntegrationEvent> Enqueued { get; } = [];
        public void Enqueue(IIntegrationEvent @event) => Enqueued.Add(@event);
    }
}
