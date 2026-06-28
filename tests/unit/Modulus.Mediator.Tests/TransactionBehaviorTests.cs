using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Xunit;

namespace Modulus.Mediator.Tests;

[Trait("Category", "Unit")]
public sealed class TransactionBehaviorTests
{
    // ── Bypass / wiring tests (no database needed) ──────────────────

    [Fact]
    public async Task NoDbContextRegistered_CallsNextDirectly()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var behavior = new TransactionBehavior<PlainCommand, string>(sp);
        var called = false;

        var result = await behavior.HandleAsync(
            new PlainCommand(),
            () => { called = true; return Task.FromResult("ok"); },
            default);

        result.Should().Be("ok");
        called.Should().BeTrue();
    }

    [Fact]
    public async Task QueryRequest_BypassesTransaction()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TxDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        var sp = services.BuildServiceProvider();
        var behavior = new TransactionBehavior<PlainQuery, string>(sp);

        var result = await behavior.HandleAsync(
            new PlainQuery(),
            () => Task.FromResult("query-ok"),
            default);

        result.Should().Be("query-ok");
    }

    [Fact]
    public async Task SkipTransactionAttribute_BypassesTransaction()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TxDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        var sp = services.BuildServiceProvider();
        var behavior = new TransactionBehavior<SkippedCommand, string>(sp);

        var result = await behavior.HandleAsync(
            new SkippedCommand(),
            () => Task.FromResult("skipped-ok"),
            default);

        result.Should().Be("skipped-ok");
    }

    [Fact]
    public async Task HandlerSucceeds_ReturnsResult()
    {
        using var fixture = new SqliteFixture();
        var sp = fixture.BuildScope();

        var behavior = new TransactionBehavior<PlainCommand, int>(sp);

        var result = await behavior.HandleAsync(
            new PlainCommand(),
            () => Task.FromResult(42),
            default);

        result.Should().Be(42);
    }

    [Fact]
    public async Task HandlerThrows_RethrowsOriginalException()
    {
        using var fixture = new SqliteFixture();
        var sp = fixture.BuildScope();
        var behavior = new TransactionBehavior<PlainCommand, int>(sp);

        var act = async () => await behavior.HandleAsync(
            new PlainCommand(),
            () => throw new InvalidOperationException("boom"),
            default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    // ── Transactional semantics (SQLite) ────────────────────────────

    [Fact]
    public async Task HandlerSucceeds_ChangesAreCommitted()
    {
        using var fixture = new SqliteFixture();
        await fixture.CreateSchemaAsync();

        var sp = fixture.BuildScope();
        var behavior = new TransactionBehavior<PlainCommand, int>(sp);

        var result = await behavior.HandleAsync(
            new PlainCommand(),
            async () =>
            {
                var db = sp.GetRequiredService<TxDbContext>();
                db.Items.Add(new TxItem { Name = "A" });
                await db.SaveChangesAsync();
                return 1;
            },
            default);

        result.Should().Be(1);
        (await fixture.CountItemsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HandlerThrowsAfterSave_ChangesAreRolledBack()
    {
        using var fixture = new SqliteFixture();
        await fixture.CreateSchemaAsync();

        var sp = fixture.BuildScope();
        var behavior = new TransactionBehavior<PlainCommand, int>(sp);

        var act = async () => await behavior.HandleAsync(
            new PlainCommand(),
            async () =>
            {
                var db = sp.GetRequiredService<TxDbContext>();
                db.Items.Add(new TxItem { Name = "A" });
                await db.SaveChangesAsync();          // within ambient tx
                throw new InvalidOperationException("boom");
            },
            default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CountItemsAsync()).Should().Be(0, "the EF transaction should roll back the insert");
    }

    // ── Fixture: shared-cache in-memory SQLite so multiple DbContext
    //    instances (one per scope) see the same database. ────────────
    private sealed class SqliteFixture : IDisposable
    {
        private readonly string _datasource = "tx-" + Guid.NewGuid().ToString("N");
        private readonly SqliteConnection _keepAlive;
        private readonly ServiceProvider _root;

        public SqliteFixture()
        {
            // Keep one connection open so the in-memory DB persists for the
            // lifetime of the fixture. Other contexts connect via the shared
            // cache so they see committed data.
            _keepAlive = new SqliteConnection(
                $"DataSource={_datasource};Mode=Memory;Cache=Shared");
            _keepAlive.Open();

            var services = new ServiceCollection();
            services.AddDbContext<TxDbContext>(o =>
                o.UseSqlite($"DataSource={_datasource};Mode=Memory;Cache=Shared"));
            // Mirror AddModuleDatabase: register also as DbContext so that
            // TransactionBehavior can discover the context.
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<TxDbContext>());
            services.AddLogging();
            _root = services.BuildServiceProvider();
        }

        public IServiceProvider BuildScope()
            => _root.CreateScope().ServiceProvider;

        public async Task CreateSchemaAsync()
        {
            using var scope = _root.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<TxDbContext>()
                .Database.EnsureCreatedAsync();
        }

        public async Task<int> CountItemsAsync()
        {
            using var scope = _root.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<TxDbContext>()
                .Items.CountAsync();
        }

        public void Dispose()
        {
            _root.Dispose();
            _keepAlive.Dispose();
        }
    }

    // ── Test doubles ────────────────────────────────────────────────
    public sealed class TxDbContext(DbContextOptions<TxDbContext> options) : DbContext(options)
    {
        public DbSet<TxItem> Items => Set<TxItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TxItem>().HasKey(x => x.Id);
    }

    public sealed class TxItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public sealed record PlainCommand : ICommand<int>;

    public sealed record PlainQuery : IQuery<string>;

    [SkipTransaction]
    public sealed record SkippedCommand : ICommand<string>;
}
