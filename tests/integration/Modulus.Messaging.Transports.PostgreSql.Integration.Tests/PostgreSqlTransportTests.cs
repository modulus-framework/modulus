using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class PostgreSqlTransportTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task FR_01_FR_02_publish_and_send_round_trip_through_the_queue_table()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);

        var orderId = Guid.NewGuid();
        await host.Bus.Publish(new OrderPlaced(orderId));
        await host.Bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(orderId));

        (await WaitUntil(() => received.OrderIds.Count == 2)).Should().BeTrue();
        received.OrderIds.Should().AllBeEquivalentTo(orderId);
        received.MessageIds.Distinct().Should().HaveCount(2, "each delivery carries its own message id");
    }

    [Fact]
    public async Task FR_07_FR_08_a_failing_message_is_redelivered_then_parked_in_error_with_its_fault()
    {
        var endpoint = Endpoint();
        await using var host = await StartBus(b => b.AddConsumer<AlwaysFailingConsumer, OrderPlaced>(endpoint, o =>
            o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(100))));

        await host.Bus.Publish(new OrderPlaced(Guid.NewGuid()));

        var parked = await WaitForRow($"{endpoint}_error");
        parked.Should().NotBeNull("the message should be parked after its redelivery fails");
        parked!.Headers["mod-error-message"].Should().Be("boom");
        parked.Headers["mod-redelivery-count"].Should().Be("1");
    }

    [Fact]
    public async Task FR_09_a_message_no_consumer_handles_is_parked_in_skipped()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);

        await host.Bus.Send(new Uri($"queue:{endpoint}"), new Ping());

        (await WaitForRow($"{endpoint}_skipped")).Should().NotBeNull();
        received.OrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RULE_02_a_message_in_flight_at_shutdown_is_released_to_the_next_host()
    {
        var endpoint = Endpoint();
        var first = new Received { BlockForever = true };
        var orderId = Guid.NewGuid();

        var firstHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), first, stopTimeout: TimeSpan.FromMilliseconds(300));
        await firstHost.Bus.Publish(new OrderPlaced(orderId));
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The consumer never finishes, so the stop times out, aborts the attempt and releases the row.
        await firstHost.StopAsync();
        await firstHost.DisposeAsync();

        var second = new Received();
        await using var secondHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), second);

        (await WaitUntil(() => second.OrderIds.Count == 1)).Should().BeTrue();
        second.OrderIds.Should().ContainSingle().Which.Should().Be(orderId);
    }

    [Fact]
    public async Task RULE_02_competing_hosts_handle_each_message_exactly_once()
    {
        const int count = 300;
        var endpoint = Endpoint();
        var first = new Received();
        var second = new Received();
        await using var firstHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), first);
        await using var secondHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), second);

        for (var i = 0; i < count; i++)
        {
            await firstHost.Bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(Guid.NewGuid()));
        }

        (await WaitUntil(() => first.OrderIds.Count + second.OrderIds.Count >= count)).Should().BeTrue();
        await Task.Delay(300);

        var all = first.OrderIds.Concat(second.OrderIds).ToList();
        all.Should().HaveCount(count, "no message is handled twice or dropped");
        all.Distinct().Should().HaveCount(count);
        second.OrderIds.Should().NotBeEmpty("both hosts take part in the work");
    }

    [Fact]
    public async Task RULE_02_a_claim_held_by_a_dead_host_is_received_after_its_lease_expires()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);
        var orderId = Guid.NewGuid();

        // A row another host claimed and then died holding: locked until the lease runs out.
        await using (var command = database.DataSource().CreateCommand("""
            INSERT INTO messaging_queue (queue, message_id, message_type, consumer_key, body, headers, lock_token, locked_until)
            VALUES (@queue, @id, @type, NULL, @body, '{}'::jsonb, gen_random_uuid(), now() + interval '2 seconds')
            """))
        {
            command.Parameters.AddWithValue("queue", endpoint);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("type", typeof(OrderPlaced).AssemblyQualifiedName!);
            command.Parameters.AddWithValue("body", $"{{\"orderId\":\"{orderId}\"}}");
            await command.ExecuteNonQueryAsync();
        }

        (await WaitUntil(() => received.OrderIds.Count == 1, TimeSpan.FromSeconds(20))).Should().BeTrue();
        received.OrderIds.Should().ContainSingle().Which.Should().Be(orderId);
    }

    [Fact]
    public async Task D3_a_row_claimed_past_the_delivery_limit_is_parked_without_running_the_consumer()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);
        var orderId = Guid.NewGuid();

        // A row already claimed 20 times (a crash loop): its next claim is delivery 21, over the default limit of 20.
        await using (var command = database.DataSource().CreateCommand("""
            INSERT INTO messaging_queue (queue, message_id, message_type, consumer_key, body, headers, delivery_count)
            VALUES (@queue, @id, @type, NULL, @body, '{}'::jsonb, 20)
            """))
        {
            command.Parameters.AddWithValue("queue", endpoint);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("type", typeof(OrderPlaced).AssemblyQualifiedName!);
            command.Parameters.AddWithValue("body", $"{{\"orderId\":\"{orderId}\"}}");
            await command.ExecuteNonQueryAsync();
        }

        var parked = await WaitForRow($"{endpoint}_error");
        parked.Should().NotBeNull("the row is parked once it is claimed over the limit");
        parked!.Headers["mod-error-message"].Should().Contain("delivered 21 times");
        received.OrderIds.Should().BeEmpty("no consumer runs for a delivery past the limit");
    }

    [Fact]
    public async Task FR_12_a_scheduled_send_arrives_at_its_time_on_postgresql()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);
        var orderId = Guid.NewGuid();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await host.Scheduler.ScheduleSend(new Uri($"queue:{endpoint}"), DateTimeOffset.UtcNow.AddSeconds(1.5), new OrderPlaced(orderId));

        await Task.Delay(400);
        received.OrderIds.Should().BeEmpty("the message is not due yet");

        (await WaitUntil(() => received.OrderIds.Count == 1, TimeSpan.FromSeconds(20))).Should().BeTrue();
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.4), "it arrives at its time, not early");
        received.OrderIds.Should().ContainSingle().Which.Should().Be(orderId);
    }

    [Fact]
    public async Task FR_12_a_cancelled_scheduled_message_never_arrives_on_postgresql()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);

        var token = await host.Scheduler.ScheduleSend(new Uri($"queue:{endpoint}"), DateTimeOffset.UtcNow.AddSeconds(1), new OrderPlaced(Guid.NewGuid()));

        (await host.Scheduler.CancelScheduled(token)).Should().BeTrue();
        await Task.Delay(1500);

        received.OrderIds.Should().BeEmpty("a cancelled message never arrives");
        (await host.Scheduler.CancelScheduled(token)).Should().BeFalse();
    }

    [Fact]
    public async Task FR_13_an_expired_message_goes_to_skipped_unprocessed_on_postgresql()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint, o => o.TimeToLive = TimeSpan.FromMilliseconds(200)), received);

        // Expiry is counted from the send (200 ms); the message is due after 1 s, so it is already expired when it arrives.
        await host.Scheduler.ScheduleSend(new Uri($"queue:{endpoint}"), DateTimeOffset.UtcNow.AddSeconds(1), new OrderPlaced(Guid.NewGuid()));

        (await WaitForRow($"{endpoint}_skipped")).Should().NotBeNull();
        received.OrderIds.Should().BeEmpty("an expired message is never processed");
    }

    [Fact]
    public async Task FR_13_higher_priority_is_consumed_first_on_postgresql()
    {
        var endpoint = Endpoint();
        var received = new Received();
        void Consumers(MessagingBusBuilder b)
        {
            b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint);
            b.AddConsumer<UrgentConsumer, UrgentOrder>(endpoint);
        }

        // Queue the work while no host polls, then start a host: the urgent message must come out first.
        await using (var first = await StartBus(Consumers, received))
        {
            await first.StopAsync();
            for (var i = 0; i < 50; i++)
            {
                await first.Bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(Guid.NewGuid()));
            }

            await first.Bus.Send(new Uri($"queue:{endpoint}"), new UrgentOrder(Guid.Empty));
        }

        await using var second = await StartBus(Consumers, received);
        (await WaitUntil(() => received.OrderIds.Count == 51, TimeSpan.FromSeconds(30))).Should().BeTrue();
        received.OrderIds.First().Should().Be(Guid.Empty, "the urgent message has the highest priority");
    }

    [Fact]
    public async Task FR_14_messages_of_one_partition_are_processed_in_send_order_across_two_hosts()
    {
        var endpoint = Endpoint();
        var log = new KeyedLog();
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<KeyedOrderConsumer, KeyedOrder>(endpoint, o => o.Ordered = true);

        // Queue the work while no host polls, then start two competing hosts.
        await using (var first = await StartHost(Consumers, s => s.AddSingleton(log)))
        {
            await first.StopAsync();
            for (var seq = 0; seq < 100; seq++)
            {
                foreach (var key in new[] { "a", "b", "c", "d" })
                {
                    await first.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder(key, seq));
                }
            }
        }

        await using var second = await StartHost(Consumers, s => s.AddSingleton(log));
        await using var third = await StartHost(Consumers, s => s.AddSingleton(log));
        (await WaitUntil(() => log.Done.Count == 400, TimeSpan.FromSeconds(60))).Should().BeTrue();

        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            log.Done.Where(m => m.Key == key).Select(m => m.Seq)
                .Should().Equal(Enumerable.Range(0, 100), $"partition {key} is handled in send order by any host");
        }
    }

    [Fact]
    public async Task FR_14_a_retried_message_keeps_its_place_in_its_partition()
    {
        var endpoint = Endpoint();
        var log = new KeyedLog { FailOnce = new KeyedOrder("a", 0) };
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<KeyedOrderConsumer, KeyedOrder>(endpoint, o =>
        {
            o.Ordered = true;
            o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(300));
        });

        await using (var first = await StartHost(Consumers, s => s.AddSingleton(log)))
        {
            await first.StopAsync();
            for (var seq = 0; seq < 5; seq++)
            {
                await first.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder("a", seq));
            }

            await first.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder("b", 0));
        }

        await using var host = await StartHost(Consumers, s => s.AddSingleton(log));
        (await WaitUntil(() => log.Done.Count == 6, TimeSpan.FromSeconds(30))).Should().BeTrue();

        log.Done.Where(m => m.Key == "a").Select(m => m.Seq).Should().Equal([0, 1, 2, 3, 4],
            "the retried message 0 finishes before the messages sent after it start");
    }

    [Fact]
    public async Task FR_14_ordered_messages_stay_in_send_order_under_concurrency_16()
    {
        var endpoint = Endpoint();
        var log = new KeyedLog();
        var gauge = new ConcurrencyGauge();
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<SlowKeyedOrderConsumer, KeyedOrder>(endpoint, o =>
        {
            o.Ordered = true;
            o.ConcurrentMessageLimit = 16;
        });

        // Queue the work while no host polls, then start two hosts that each run 16 at once.
        await using (var first = await StartHost(Consumers, s => s.AddSingleton(log).AddSingleton(gauge)))
        {
            await first.StopAsync();
            for (var seq = 0; seq < 100; seq++)
            {
                foreach (var key in new[] { "a", "b", "c", "d", "e" })
                {
                    await first.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder(key, seq));
                }
            }
        }

        await using var second = await StartHost(Consumers, s => s.AddSingleton(log).AddSingleton(gauge));
        await using var third = await StartHost(Consumers, s => s.AddSingleton(log).AddSingleton(gauge));
        (await WaitUntil(() => log.Done.Count == 500, TimeSpan.FromSeconds(60))).Should().BeTrue();

        foreach (var key in new[] { "a", "b", "c", "d", "e" })
        {
            log.Done.Where(m => m.Key == key).Select(m => m.Seq)
                .Should().Equal(Enumerable.Range(0, 100), $"partition {key} is handled in send order under concurrency 16");
        }

        gauge.Peak.Should().BeGreaterThan(1, "different partitions run at the same time");
        gauge.Peak.Should().BeLessOrEqualTo(32, "each of the two hosts runs at most 16");
    }

    [Fact]
    public async Task FR_14_a_single_host_runs_up_to_the_concurrency_limit()
    {
        var endpoint = Endpoint();
        var log = new KeyedLog();
        var gauge = new ConcurrencyGauge();
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<SlowKeyedOrderConsumer, KeyedOrder>(endpoint, o => o.ConcurrentMessageLimit = 16);

        await using (var first = await StartHost(Consumers, s => s.AddSingleton(log).AddSingleton(gauge)))
        {
            await first.StopAsync();
            for (var seq = 0; seq < 200; seq++)
            {
                await first.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder($"k{seq}", 0));
            }
        }

        await using var host = await StartHost(Consumers, s => s.AddSingleton(log).AddSingleton(gauge));
        (await WaitUntil(() => log.Done.Count == 200, TimeSpan.FromSeconds(60))).Should().BeTrue();

        gauge.Peak.Should().BeGreaterThan(1, "a limit of 16 lets messages overlap");
        gauge.Peak.Should().BeLessOrEqualTo(16);
    }

    private static string Endpoint() => $"orders-{Guid.NewGuid():N}";

    private async Task<TestHost> StartHost(Action<MessagingBusBuilder> consumers, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        services.AddModulusMessaging(b =>
        {
            consumers(b);
            b.UsePostgreSql(o =>
            {
                var options = database.Options();
                o.ConnectionString = options.ConnectionString;
                o.StopTimeout = options.StopTimeout;
                o.LeaseDuration = options.LeaseDuration;
                o.PollInterval = options.PollInterval;
            });
        });

        var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        return new TestHost(provider, host, provider.GetRequiredService<IBus>(), provider.GetRequiredService<IMessageScheduler>());
    }

    private async Task<TestHost> StartBus(
        Action<MessagingBusBuilder> consumers,
        Received? received = null,
        TimeSpan? stopTimeout = null)
    {
        received ??= new Received();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            consumers(b);
            b.UsePostgreSql(o =>
            {
                var options = database.Options(stopTimeout);
                o.ConnectionString = options.ConnectionString;
                o.StopTimeout = options.StopTimeout;
                o.LeaseDuration = options.LeaseDuration;
                o.PollInterval = options.PollInterval;
            });
        });

        var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        return new TestHost(provider, host, provider.GetRequiredService<IBus>(), provider.GetRequiredService<IMessageScheduler>());
    }

    private async Task<ParkedRow?> WaitForRow(string queue)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = database.DataSource().CreateCommand(
                "SELECT headers::text FROM messaging_queue WHERE queue = @queue ORDER BY id LIMIT 1");
            command.Parameters.AddWithValue("queue", queue);
            var headers = await command.ExecuteScalarAsync() as string;
            if (headers is not null)
            {
                return new ParkedRow(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headers)!);
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static async Task SendWhenRecovered(IBus bus, string endpoint, Guid orderId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(orderId));
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private sealed record ParkedRow(Dictionary<string, string> Headers);

    private sealed class TestHost(ServiceProvider provider, IHostedService host, IBus bus, IMessageScheduler scheduler) : IAsyncDisposable
    {
        public IBus Bus { get; } = bus;

        public IMessageScheduler Scheduler { get; } = scheduler;

        public Task StopAsync() => host.StopAsync(CancellationToken.None);

        public async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
