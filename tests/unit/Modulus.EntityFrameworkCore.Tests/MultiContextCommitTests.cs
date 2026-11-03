namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MultiContextCommitTests : IAsyncDisposable
{
    private readonly SqliteConnection _connA;
    private readonly SqliteConnection _connB;
    private readonly ServiceProvider _sp;
    private readonly ContextA _a;
    private ContextB B => _sp.GetServices<DbContext>().OfType<ContextB>().Single();

    public MultiContextCommitTests()
    {
        var name = Guid.NewGuid().ToString("N");
        _connA = new SqliteConnection($"DataSource=multi-a-{name};Mode=Memory;Cache=Shared");
        _connB = new SqliteConnection($"DataSource=multi-b-{name};Mode=Memory;Cache=Shared");
        _connA.Open();
        _connB.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<DomainEventDispatcher>();
        services.TryAddScoped<IDeferredDomainEventQueue, DeferredDomainEventQueue>();
        services.AddModuleDatabase<ContextA>(o => o.UseSqlite(_connA));
        services.AddModuleDatabase<ContextB>(o => o.UseSqlite(_connB).AddInterceptors(new FailingCommitInterceptor()));
        _sp = services.BuildServiceProvider();
        _a = _sp.GetServices<DbContext>().OfType<ContextA>().Single();
        _a.Database.EnsureCreated();
        B.Database.EnsureCreated();
    }

    [Fact]
    public async Task PartialCommit_IsNotRetried_AndSurfacesAsPartialCommitFailure()
    {
        var runs = 0;
        var behavior = new TransactionBehavior<TestCommand, bool>(
            _sp, new TransactionRuntimeOptions(TransactionMode.AllContexts));

        var act = () => behavior.HandleAsync(new TestCommand(), async () =>
        {
            Interlocked.Increment(ref runs);
            _a.Rows.Add(new Row());
            await _a.SaveChangesAsync();
            B.Rows.Add(new Row());
            await B.SaveChangesAsync();
            return true;
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*partially committed*");
        runs.Should().Be(1, "a partially committed unit must not be re-run");

        await using var verify = new ContextA(
            new DbContextOptionsBuilder<ContextA>().UseSqlite(_connA).Options,
            new HostTenant(), new NullCurrentUser(), _sp.GetRequiredService<DomainEventDispatcher>(), _sp);
        (await verify.Rows.CountAsync()).Should().Be(1, "the context that committed keeps its write");
    }

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
        _connA.Dispose();
        _connB.Dispose();
    }

    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated commit failure");
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => true;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => new Noop();
        public IDisposable BeginScope() => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed record TestCommand;

    public sealed class Row
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }

    public sealed class ContextA(
        DbContextOptions<ContextA> options,
        ICurrentTenant tenant,
        ICurrentUser user,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, tenant, user, dispatcher, sp)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override string TablePrefix => "a_";

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Row>(b => b.HasKey(x => x.Id));
        }
    }

    public sealed class ContextB(
        DbContextOptions<ContextB> options,
        ICurrentTenant tenant,
        ICurrentUser user,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, tenant, user, dispatcher, sp)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override string TablePrefix => "b_";

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Row>(b => b.HasKey(x => x.Id));
        }
    }
}
