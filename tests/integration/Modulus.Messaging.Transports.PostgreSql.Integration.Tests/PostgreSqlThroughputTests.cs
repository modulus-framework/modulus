using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>Counts the messages a consumer has handled.</summary>
public sealed class ThroughputCounter
{
    private long _handled;

    public long Handled => Volatile.Read(ref _handled);

    public void Add() => Interlocked.Increment(ref _handled);
}

public sealed class ThroughputConsumer(ThroughputCounter counter) : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        counter.Add();
        return Task.CompletedTask;
    }
}

/// <summary>
/// NFR-05: messages per second through one PostgreSQL queue table, end to end (send to handled), on a real database. It
/// asserts that every message arrives; the rate is reported, not gated (the target is judged on the target hardware).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlThroughputTests(PostgreSqlBroker database, ITestOutputHelper output) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task NFR_05_measures_end_to_end_throughput_on_one_database()
    {
        const int count = 20000;
        var endpoint = $"throughput-{Guid.NewGuid():N}";
        var counter = new ThroughputCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<ThroughputConsumer, OrderPlaced>(endpoint, o => o.ConcurrentMessageLimit = 8);
            b.UsePostgreSql(o =>
            {
                o.ConnectionString = database.ConnectionString;
                o.PollInterval = TimeSpan.FromMilliseconds(20);
                o.BatchSize = 64;
            });
        });

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<IBus>();
        var address = new Uri($"queue:{endpoint}");

        var clock = Stopwatch.StartNew();
        const int concurrency = 64;
        for (var sent = 0; sent < count; sent += concurrency)
        {
            var batch = Enumerable.Range(0, Math.Min(concurrency, count - sent))
                .Select(_ => bus.Send(address, new OrderPlaced(Guid.NewGuid())));
            await Task.WhenAll(batch);
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(180);
        while (counter.Handled < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        clock.Stop();
        await host.StopAsync(CancellationToken.None);

        counter.Handled.Should().Be(count, "every message is handled exactly once");
        var rate = count / clock.Elapsed.TotalSeconds;
        output.WriteLine($"Handled {count} in {clock.Elapsed.TotalSeconds:F2} s = {rate:F0} msg/s on one PostgreSQL database (NFR-05 target 2,000 to 5,000 msg/s).");
    }
}
