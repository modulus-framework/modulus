using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Outbox.Npgsql;
using Modulus.Messaging.Sagas.PostgreSql;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Sagas.PostgreSql.Integration.Tests;

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

[Trait("Category", "Integration")]
public sealed class PostgreSqlSagaRepositoryTests(SagaPostgreSqlBroker database) : IClassFixture<SagaPostgreSqlBroker>
{
    [Fact]
    public async Task FR_19_a_stale_save_is_refused_on_postgresql_so_no_update_is_lost()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await CreateTableAsync(dataSource, "sagas_stale");
        var repository = new PostgreSqlSagaRepository<TallySaga>(dataSource, "sagas_stale");
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
    public async Task FR_19_concurrent_events_for_one_saga_all_apply_on_postgresql_and_none_is_lost()
    {
        var table = "sagas_bus";
        var tallyId = Guid.NewGuid();
        var retries = Enumerable.Repeat(TimeSpan.FromMilliseconds(5), 400).ToArray();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await CreateTableAsync(dataSource, table);

        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumerSaga<TallySaga>("tally-saga", o =>
                {
                    o.ConcurrentMessageLimit = 8;
                    o.Redelivery = RedeliveryPolicy.Intervals(retries);
                })
                .UseInMemory(),
            services => services
                .AddSingleton(dataSource)
                .AddSingleton<ISagaRepository<TallySaga>>(sp => new PostgreSqlSagaRepository<TallySaga>(dataSource, table)));
        var repository = harness.Services.GetRequiredService<ISagaRepository<TallySaga>>();
        var endpoint = new Uri("queue:tally-saga");

        await harness.Bus.Send(endpoint, new TallyStarted(tallyId));
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => harness.Bus.Send(endpoint, new TallyIncremented(tallyId))));

        var count = -1;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            count = (await repository.FindAsync(tallyId, CancellationToken.None))?.Count ?? -1;
            if (count == 50)
            {
                break;
            }

            await Task.Delay(50);
        }

        count.Should().Be(50, "every increment applied once, even when two workers met on the same instance");
    }

    [Fact]
    public async Task RULE_06_saga_state_written_in_a_unit_of_work_rolls_back_with_it()
    {
        const string table = "sagas_uow_rollback";
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await CreateTableAsync(dataSource, table);
        await using var unitOfWork = new NpgsqlUnitOfWork(dataSource);
        var repository = new PostgreSqlSagaRepository<TallySaga>(dataSource, table, unitOfWork);
        var tallyId = Guid.NewGuid();

        await unitOfWork.BeginAsync(CancellationToken.None);
        await repository.SaveAsync(new TallySaga { CorrelationId = tallyId }, CancellationToken.None);
        (await repository.FindAsync(tallyId, CancellationToken.None)).Should().NotBeNull("the scope reads its own uncommitted write");
        await unitOfWork.RollbackAsync(CancellationToken.None);

        var outside = new PostgreSqlSagaRepository<TallySaga>(dataSource, table);
        (await outside.FindAsync(tallyId, CancellationToken.None)).Should().BeNull("the saga state rolled back with the unit of work");
    }

    [Fact]
    public async Task RULE_06_saga_state_and_the_other_writes_of_one_unit_of_work_commit_together()
    {
        const string table = "sagas_uow_commit";
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await CreateTableAsync(dataSource, table);
        await using (var setup = dataSource.CreateCommand("CREATE TABLE IF NOT EXISTS uow_marks (id uuid NOT NULL)"))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var unitOfWork = new NpgsqlUnitOfWork(dataSource);
        var repository = new PostgreSqlSagaRepository<TallySaga>(dataSource, table, unitOfWork);
        var tallyId = Guid.NewGuid();
        var markId = Guid.NewGuid();

        await unitOfWork.BeginAsync(CancellationToken.None);
        await repository.SaveAsync(new TallySaga { CorrelationId = tallyId }, CancellationToken.None);
        await using (var mark = new NpgsqlCommand("INSERT INTO uow_marks (id) VALUES (@id)", unitOfWork.Connection!, unitOfWork.Transaction!))
        {
            mark.Parameters.AddWithValue("id", markId);
            await mark.ExecuteNonQueryAsync();
        }

        await unitOfWork.CommitAsync(CancellationToken.None);

        var outside = new PostgreSqlSagaRepository<TallySaga>(dataSource, table);
        (await outside.FindAsync(tallyId, CancellationToken.None)).Should().NotBeNull();
        await using var count = dataSource.CreateCommand("SELECT count(*) FROM uow_marks WHERE id = @id");
        count.Parameters.AddWithValue("id", markId);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1);
    }

    private static async Task CreateTableAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var command = dataSource.CreateCommand(PostgreSqlSagaRepository<TallySaga>.SchemaScript(table));
        await command.ExecuteNonQueryAsync();
    }
}
