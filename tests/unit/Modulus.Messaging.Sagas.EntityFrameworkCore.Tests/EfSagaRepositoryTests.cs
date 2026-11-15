using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Sagas.EntityFrameworkCore;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Sagas.EntityFrameworkCore.Tests;

public sealed record TallyStarted(Guid TallyId);

public sealed record TallyIncremented(Guid TallyId);

public sealed class TallySaga :
    SagaInstance,
    ICorrelatedBy<TallyStarted>,
    ICorrelatedBy<TallyIncremented>,
    IInitiatedBy<TallySaga, TallyStarted>,
    IOrchestrates<TallySaga, TallyIncremented>
{
    public int Count { get; set; }

    public static Guid CorrelationKeyOf(TallyStarted message) => message.TallyId;

    public static Guid CorrelationKeyOf(TallyIncremented message) => message.TallyId;

    public void Initiate(SagaContext<TallySaga, TallyStarted> context) => CurrentState = "Open";

    // The pause widens the window between reading the instance and saving it, so workers really meet on it.
    public void Orchestrate(SagaContext<TallySaga, TallyIncremented> context)
    {
        var seen = Count;
        Thread.Sleep(3);
        Count = seen + 1;
    }
}

public sealed class SagaTestDb(DbContextOptions<SagaTestDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyModulusSagas();
}


[Trait("Category", "Unit")]
public sealed class EfSagaRepositoryTests : IAsyncLifetime
{
    private SqliteConnection _keepAlive = null!;

    public async Task InitializeAsync()
    {
        // A shared in-memory SQLite database lives as long as one connection stays open.
        _keepAlive = new SqliteConnection($"DataSource=file:sagas-{Guid.NewGuid():N}?mode=memory&cache=shared");
        await _keepAlive.OpenAsync();
    }

    public async Task DisposeAsync() => await _keepAlive.DisposeAsync();

    [Fact]
    public async Task FR_19_a_stale_save_is_refused_on_the_database_so_no_update_is_lost()
    {
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        var repository = new EfSagaRepository<TallySaga>(db);
        var tallyId = Guid.NewGuid();
        await repository.SaveAsync(new TallySaga { CorrelationId = tallyId }, CancellationToken.None);

        var first = (await repository.FindAsync(tallyId, CancellationToken.None))!;
        var second = (await repository.FindAsync(tallyId, CancellationToken.None))!;
        first.Count++;
        await repository.SaveAsync(first, CancellationToken.None);

        second.Count++;
        Func<Task> stale = () => repository.SaveAsync(second, CancellationToken.None);

        await stale.Should().ThrowAsync<SagaConcurrencyException>();
        (await repository.FindAsync(tallyId, CancellationToken.None))!.Count.Should().Be(1);
    }

    [Fact]
    public async Task FR_19_concurrent_events_for_one_saga_all_apply_in_the_database_and_none_is_lost()
    {
        var tallyId = Guid.NewGuid();
        var retries = Enumerable.Repeat(TimeSpan.FromMilliseconds(5), 400).ToArray();
        var databaseName = $"sagas-bus-{Guid.NewGuid():N}";
        using var connection = new SqliteConnection($"DataSource=file:{databaseName}?mode=memory&cache=shared");
        await connection.OpenAsync();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumerSaga<TallySaga>("tally-saga", o =>
                {
                    o.ConcurrentMessageLimit = 8;
                    o.Redelivery = RedeliveryPolicy.Intervals(retries);
                })
                .UseInMemory(),
            services => services
                .AddDbContext<SagaTestDb>(o => o.UseSqlite($"DataSource=file:{databaseName}?mode=memory&cache=shared"))
                .AddEntityFrameworkSagaRepository<TallySaga, SagaTestDb>());
        using (var setup = harness.Services.CreateScope())
        {
            await setup.ServiceProvider.GetRequiredService<SagaTestDb>().Database.EnsureCreatedAsync();
        }

        var endpoint = new Uri("queue:tally-saga");
        await harness.Bus.Send(endpoint, new TallyStarted(tallyId));
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => harness.Bus.Send(endpoint, new TallyIncremented(tallyId))));

        var count = -1;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = harness.Services.CreateAsyncScope();
            count = (await scope.ServiceProvider.GetRequiredService<ISagaRepository<TallySaga>>().FindAsync(tallyId, CancellationToken.None))?.Count ?? -1;
            if (count == 50)
            {
                break;
            }

            await Task.Delay(50);
        }

        count.Should().Be(50, "every increment applied once, even when two workers met on the same instance");
    }

    private SagaTestDb CreateDb() =>
        new(new DbContextOptionsBuilder<SagaTestDb>().UseSqlite(_keepAlive).Options);
}
