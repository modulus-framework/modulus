using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>
/// Measures end-to-end throughput (publish to handled) on a real broker. It asserts that every message arrives;
/// the rate is reported, not gated (G2 is 25k msg/s and is judged on dedicated hardware).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqThroughputTests(RabbitMqBroker broker, ITestOutputHelper output) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task G2_measures_end_to_end_throughput_with_publisher_confirms()
    {
        const int count = 50000;
        var endpoint = $"throughput-{Guid.NewGuid():N}";
        var received = new Counter();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<CountingConsumer, OrderPlaced>(endpoint);
            b.UseRabbitMq(o =>
            {
                var options = broker.Options();
                o.HostName = options.HostName;
                o.Port = options.Port;
                o.UserName = options.UserName;
                o.Password = options.Password;
                o.StopTimeout = options.StopTimeout;
                o.Prefetch = 256;
            });
        });

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<IBus>();
        var address = new Uri($"queue:{endpoint}");

        var clock = Stopwatch.StartNew();
        // Callers publish concurrently, as a service with parallel requests does; each send still waits for its confirm.
        const int concurrency = 256;
        for (var sent = 0; sent < count; sent += concurrency)
        {
            var batch = Enumerable.Range(0, Math.Min(concurrency, count - sent))
                .Select(_ => bus.Send(address, new OrderPlaced(Guid.NewGuid())));
            await Task.WhenAll(batch);
        }

        var publishTime = clock.Elapsed;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(120);
        while (received.Value < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        clock.Stop();
        var rate = count / clock.Elapsed.TotalSeconds;
        output.WriteLine($"Delivered {received.Value}/{count} in {clock.Elapsed.TotalSeconds:F2} s = {rate:F0} msg/s; publish loop {publishTime.TotalSeconds:F2} s ({count / publishTime.TotalSeconds:F0} msg/s)");
        received.Value.Should().Be(count);

        await host.StopAsync(CancellationToken.None);
    }

    public sealed class Counter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }

    public sealed class CountingConsumer(Counter counter) : IConsumer<OrderPlaced>
    {
        public Task Consume(ConsumeContext<OrderPlaced> context)
        {
            counter.Increment();
            return Task.CompletedTask;
        }
    }
}
