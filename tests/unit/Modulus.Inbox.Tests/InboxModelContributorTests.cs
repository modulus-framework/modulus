using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using NSubstitute;
using Xunit;

namespace Modulus.Inbox.Tests;

[Trait("Category", "Unit")]
public sealed class InboxModelContributorTests
{
    [Fact]
    public async Task AddInbox_MapsInboxMessageEntityIntoModuleContext()
    {
        await using var h = await InboxHarness.BuildAsync();

        h.Db.Model.FindEntityType(typeof(InboxMessage))
            .Should().NotBeNull(
                "AddInbox must map InboxMessage into every ModuleDbContext — " +
                "previously apps had to hand-wire InboxMessageConfiguration");
    }

    [Fact]
    public async Task AddInbox_InboxTableReceivesModuleTablePrefix()
    {
        await using var h = await InboxHarness.BuildAsync();

        h.Db.Model.FindEntityType(typeof(InboxMessage))!
            .GetTableName().Should().StartWith("tst_",
                "the contributor runs before table-prefixing, like the outbox mapping");
    }

    [Fact]
    public async Task AddInbox_EfInboxStoreFromDI_ClaimsAndPersistsWithoutHandWiring()
    {
        await using var h = await InboxHarness.BuildAsync();
        await h.Db.Database.EnsureCreatedAsync();

        var store = h.Scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var claimed = await store.TryClaimAsync(
            Guid.NewGuid(), "test.event.v1", "{}", maxRetries: 3,
            claimTimeout: TimeSpan.FromMinutes(5), default);

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(InboxStatus.Processing);
        (await h.Db.Set<InboxMessage>().CountAsync()).Should().Be(1);
    }

    private sealed class InboxHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly ServiceProvider _root;

        private InboxHarness(SqliteConnection conn, ServiceProvider root, IServiceScope scope, TestDbContext db)
        {
            _conn = conn;
            _root = root;
            Scope = scope;
            Db = db;
        }

        public IServiceScope Scope { get; }
        public TestDbContext Db { get; }

        public static async Task<InboxHarness> BuildAsync()
        {
            var conn = new SqliteConnection("DataSource=:memory:");
            await conn.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<TestDbContext>(o => o.UseSqlite(conn));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestDbContext>());

            services.AddSingleton<ICurrentTenant>(Substitute.For<ICurrentTenant>());
            services.AddSingleton<ICurrentUser>(Substitute.For<ICurrentUser>());
            services.AddScoped<DomainEventDispatcher>();

            services.AddInbox<TestDbContext>();

            var root = services.BuildServiceProvider();
            var scope = root.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            return new InboxHarness(conn, root, scope, db);
        }

        public async ValueTask DisposeAsync()
        {
            await _root.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    internal sealed class TestDbContext(
        DbContextOptions<TestDbContext> opts,
        ICurrentTenant tenant,
        ICurrentUser user,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(opts, tenant, user, dispatcher, sp)
    {
        protected override string TablePrefix => "tst_";
    }
}
