using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Inbox.Npgsql;
using Modulus.Messaging.Transports.InMemory;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Outbox.Npgsql.Integration.Tests;

public sealed record Ping(Guid Id);

public sealed class PingLog
{
    public ConcurrentQueue<Guid> Handled { get; } = new();
}

public sealed class PingRecorder(PingLog log) : IConsumer<Ping>
{
    public Task Consume(ConsumeContext<Ping> context)
    {
        log.Handled.Enqueue(context.Message.Id);
        return Task.CompletedTask;
    }
}

/// <summary>Writes a row in the consume transaction, then fails on the first attempt: the write must roll back with the attempt.</summary>
public sealed class WriteThenFailOncePing(PingLog log, INpgsqlUnitOfWork uow, Attempts attempts) : IConsumer<Ping>
{
    public async Task Consume(ConsumeContext<Ping> context)
    {
        await using (var command = new NpgsqlCommand("INSERT INTO npgsql_writes (id) VALUES (@id)", uow.Connection!, uow.Transaction))
        {
            command.Parameters.AddWithValue("id", context.Message.Id);
            await command.ExecuteNonQueryAsync(context.CancellationToken);
        }

        if (attempts.Next() == 1)
        {
            throw new InvalidOperationException("the first attempt fails after its write");
        }

        log.Handled.Enqueue(context.Message.Id);
    }
}

public sealed class Attempts
{
    private int _value;

    public int Next() => Interlocked.Increment(ref _value);

    public int Value => Volatile.Read(ref _value);
}

/// <summary>The outbox and inbox on PostgreSQL, against the bus (D-Npgsql, plan M1 outbox and inbox on Npgsql).</summary>
[Trait("Category", "Integration")]
public sealed class NpgsqlOutboxInboxTests(NpgsqlBroker database) : IClassFixture<NpgsqlBroker>
{
    [Fact]
    public async Task RULE_01_a_publish_in_a_committed_transaction_is_sent_once_after_the_commit()
    {
        var endpoint = Endpoint();
        var id = Guid.NewGuid();
        await using var host = await StartHost(endpoint);
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
            await uow.BeginAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBus>().Publish(new Ping(id));
            host.Log.Handled.Should().BeEmpty("nothing is sent before the commit");
            await uow.CommitAsync(CancellationToken.None);
        }

        await DispatchOnceAsync(host);

        (await WaitUntil(() => host.Log.Handled.Contains(id))).Should().BeTrue();
    }

    [Fact]
    public async Task RULE_01_a_publish_in_a_rolled_back_transaction_is_never_sent()
    {
        var endpoint = Endpoint();
        var id = Guid.NewGuid();
        await using var host = await StartHost(endpoint);
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
            await uow.BeginAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBus>().Publish(new Ping(id));
            await uow.RollbackAsync(CancellationToken.None);
        }

        await DispatchOnceAsync(host);
        await Task.Delay(300);

        host.Log.Handled.Should().NotContain(id);
    }

    [Fact]
    public async Task RULE_01_a_publish_without_a_transaction_is_refused()
    {
        var endpoint = Endpoint();
        await using var host = await StartHost(endpoint);
        await using var scope = host.Provider.CreateAsyncScope();

        var publish = async () => await scope.ServiceProvider.GetRequiredService<IBus>().Publish(new Ping(Guid.NewGuid()));

        await publish.Should().ThrowAsync<InvalidOperationException>().WithMessage("*transaction*");
    }

    [Fact]
    public async Task RULE_01_competing_dispatchers_send_each_row_exactly_once()
    {
        var endpoint = Endpoint();
        var ids = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();
        await using var host = await StartHost(endpoint);
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
            await uow.BeginAsync(CancellationToken.None);
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            foreach (var id in ids)
            {
                await bus.Publish(new Ping(id));
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        var dispatchers = Enumerable.Range(0, 3).Select(_ => DispatchUntilEmptyAsync(host));
        await Task.WhenAll(dispatchers);

        (await WaitUntil(() => host.Log.Handled.Count == ids.Count)).Should().BeTrue();
        host.Log.Handled.Should().HaveCount(ids.Count).And.OnlyHaveUniqueItems("each row is sent once across the dispatchers");
    }

    [Fact]
    public async Task RULE_04_a_redelivered_message_is_handled_once_and_counted_by_the_inbox()
    {
        var endpoint = Endpoint();
        var id = Guid.NewGuid();
        await using var host = await StartHost(endpoint);
        var envelope = new MessageEnvelope(id, endpoint, new Ping(id), new Dictionary<string, string>(), typeof(PingRecorder).FullName);
        var transport = host.Provider.GetRequiredService<IMessageTransport>();

        await transport.Send(envelope, CancellationToken.None);
        await transport.Send(envelope, CancellationToken.None);

        (await WaitUntil(() => host.Log.Handled.Contains(id))).Should().BeTrue();
        await Task.Delay(300);
        host.Log.Handled.Count(x => x == id).Should().Be(1, "the second delivery is a duplicate");
        (await CountInboxRowsAsync(id)).Should().Be(1);
    }

    [Fact]
    public async Task RULE_06_a_failed_attempt_rolls_back_its_write_and_its_inbox_record_so_the_redelivery_applies_once()
    {
        var endpoint = Endpoint();
        var id = Guid.NewGuid();
        await database.DataSource().CreateCommand("CREATE TABLE IF NOT EXISTS npgsql_writes (id uuid NOT NULL)").ExecuteNonQueryAsync();
        await using (var truncate = database.DataSource().CreateCommand("DELETE FROM npgsql_writes"))
        {
            await truncate.ExecuteNonQueryAsync();
        }

        var attempts = new Attempts();
        await using var host = await StartHost(endpoint, consumer: (b, e) => b.AddConsumer<WriteThenFailOncePing, Ping>(e, o =>
            o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(50))), extra: services => services.AddSingleton(attempts));
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
            await uow.BeginAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBus>().Send(new Uri($"queue:{endpoint}"), new Ping(id));
            await uow.CommitAsync(CancellationToken.None);
        }

        await DispatchOnceAsync(host);

        (await WaitUntil(() => attempts.Value == 2 && host.Log.Handled.Contains(id))).Should().BeTrue();
        await using var count = database.DataSource().CreateCommand("SELECT count(*) FROM npgsql_writes WHERE id = @id");
        count.Parameters.AddWithValue("id", id);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1, "the failed attempt's write rolled back");
        // The bus assigns the message id at send time, so this message's inbox row is the only one in the cleared table.
        (await CountAllInboxRowsAsync()).Should().Be(1, "the failed attempt's inbox record rolled back, the second attempt's is kept");
    }

    [Fact]
    public async Task RULE_01_a_scheduled_message_in_the_outbox_can_be_cancelled_until_it_is_sent()
    {
        var endpoint = Endpoint();
        await using var host = await StartHost(endpoint);
        Guid token;
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
            await uow.BeginAsync(CancellationToken.None);
            token = await scope.ServiceProvider.GetRequiredService<IMessageScheduler>()
                .ScheduleSend(new Uri($"queue:{endpoint}"), DateTimeOffset.UtcNow.AddHours(1), new Ping(Guid.NewGuid()));
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var cancel = host.Provider.CreateAsyncScope();
        var cancelUow = cancel.ServiceProvider.GetRequiredService<INpgsqlUnitOfWork>();
        await cancelUow.BeginAsync(CancellationToken.None);
        var cancelled = await cancel.ServiceProvider.GetRequiredService<IMessageScheduler>().CancelScheduled(token, CancellationToken.None);
        await cancelUow.CommitAsync(CancellationToken.None);

        cancelled.Should().BeTrue();
    }

    private string Endpoint() => $"npgsql-{Guid.NewGuid():N}";

    private async Task<Host> StartHost(string endpoint, Action<MessagingBusBuilder, string>? consumer = null, Action<IServiceCollection>? extra = null)
    {
        var log = new PingLog();
        var services = new ServiceCollection();
        services.AddSingleton(database.DataSource());
        services.AddSingleton(log);
        extra?.Invoke(services);
        services.AddModulusMessaging(b =>
        {
            if (consumer is null)
            {
                b.AddConsumer<PingRecorder, Ping>(endpoint);
            }
            else
            {
                consumer(b, endpoint);
            }

            b.UseInMemory();
        });
        services.AddModulusMessagingNpgsqlOutbox(o => o.RunDispatcher = false);
        services.AddModulusMessagingNpgsqlInbox();

        // The tables are shared by the tests of this class: create them, then start each test from empty tables.
        await using (var schema = database.DataSource().CreateCommand(
                         MessagingNpgsqlOutboxExtensions.OutboxSchemaScript() + "\n" + MessagingNpgsqlInboxExtensions.InboxSchemaScript()))
        {
            await schema.ExecuteNonQueryAsync();
        }

        await using (var clear = database.DataSource().CreateCommand("DELETE FROM messaging_outbox; DELETE FROM messaging_inbox"))
        {
            await clear.ExecuteNonQueryAsync();
        }

        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return new Host(provider, log);
    }

    private async Task DispatchOnceAsync(Host host) =>
        await NpgsqlOutboxDispatch.DispatchOnceAsync(database.DataSource(), host.Provider.GetRequiredService<IMessageTransport>(), new NpgsqlOutboxOptions(), CancellationToken.None);

    private async Task DispatchUntilEmptyAsync(Host host)
    {
        while (await NpgsqlOutboxDispatch.DispatchOnceAsync(database.DataSource(), host.Provider.GetRequiredService<IMessageTransport>(), new NpgsqlOutboxOptions(), CancellationToken.None) > 0)
        {
        }
    }

    private async Task<long> CountAllInboxRowsAsync()
    {
        await using var count = database.DataSource().CreateCommand("SELECT count(*) FROM messaging_inbox");
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private async Task<long> CountInboxRowsAsync(Guid id)
    {
        await using var count = database.DataSource().CreateCommand("SELECT count(*) FROM messaging_inbox WHERE message_id = @id");
        count.Parameters.AddWithValue("id", id);
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    private sealed class Host(ServiceProvider provider, PingLog log) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public PingLog Log { get; } = log;

        public async ValueTask DisposeAsync()
        {
            foreach (var hosted in Provider.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }

            await Provider.DisposeAsync();
        }
    }
}
