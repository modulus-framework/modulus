using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging;
using Modulus.Messaging.Transports.InMemory;

namespace Modulus.Benchmarks;

/// <summary>Counts handled messages down to zero and completes a task when the count is reached.</summary>
public sealed class BenchCountdown
{
    private int _remaining;
    private TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts a new batch and returns the completion to await (its <c>Task</c> finishes when the batch is handled).</summary>
    public TaskCompletionSource Reset(int count)
    {
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _remaining, count);
        return _done;
    }

    public void Handled()
    {
        if (Interlocked.Decrement(ref _remaining) == 0)
        {
            _done.TrySetResult();
        }
    }
}

public sealed class BenchCountingConsumer(BenchCountdown countdown) : IConsumer<BenchPing>
{
    public Task Consume(ConsumeContext<BenchPing> context)
    {
        countdown.Handled();
        return Task.CompletedTask;
    }
}

/// <summary>
/// NFR-03: messages per second on the in-memory transport (no broker, no disk). A batch of messages is sent and the
/// batch is done when the last one is handled. The benchmark reports time per message; messages per second is 1 / that.
/// Concurrency 8 is the consumer limit the requirement's 8 cores point at.
/// </summary>
[MemoryDiagnoser]
public class MessagingThroughputBenchmarks
{
    private const int Messages = 10_000;

    private ServiceProvider _provider = null!;
    private IHostedService _host = null!;
    private IBus _bus = null!;
    private BenchCountdown _countdown = null!;
    private Uri _address = null!;

    [Params(1, 8)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _countdown = new BenchCountdown();
        var services = new ServiceCollection();
        services.AddSingleton(_countdown);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<BenchCountingConsumer, BenchPing>("bench-throughput", o => o.ConcurrentMessageLimit = Concurrency);
            b.UseInMemory();
        });
        _provider = services.BuildServiceProvider();
        _host = _provider.GetServices<IHostedService>().Single();
        await _host.StartAsync(CancellationToken.None);
        _bus = _provider.GetRequiredService<IBus>();
        _address = new Uri("queue:bench-throughput");
    }

    [Benchmark(OperationsPerInvoke = Messages)]
    public async Task SendAndHandleBatch()
    {
        var done = _countdown.Reset(Messages);
        for (var i = 0; i < Messages; i++)
        {
            await _bus.Send(_address, new BenchPing());
        }

        await done.Task;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _host.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }
}

/// <summary>
/// NFR-07: how long building the bus, registering 100 consumers and starting the host takes. The target is under 50 ms.
/// The 100 registrations use one consumer type on 100 endpoints, which is what the startup path does per registration.
/// </summary>
[MemoryDiagnoser]
public class MessagingStartupBenchmarks
{
    [Benchmark]
    public async Task Start_with_100_consumers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BenchCountdown());
        services.AddModulusMessaging(b =>
        {
            for (var i = 0; i < 100; i++)
            {
                b.AddConsumer<BenchCountingConsumer, BenchPing>($"bench-startup-{i}");
            }

            b.UseInMemory();
        });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);
    }
}
