using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging;
using Modulus.Messaging.Transports.InMemory;

namespace Modulus.Benchmarks;

public sealed record BenchPing;

/// <summary>Completes the wait of the current send when its message is handled.</summary>
public sealed class BenchHandled
{
    private TaskCompletionSource _current = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Next()
    {
        _current = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _current;
    }

    public void Complete() => _current.TrySetResult();
}

public sealed class BenchPingConsumer(BenchHandled handled) : IConsumer<BenchPing>
{
    public Task Consume(ConsumeContext<BenchPing> context)
    {
        handled.Complete();
        return Task.CompletedTask;
    }
}

/// <summary>Adds the median (p50) column to the default columns.</summary>
public sealed class MedianConfig : ManualConfig
{
    public MedianConfig() => AddColumn(StatisticColumn.Median);
}

/// <summary>
/// NFR-06: the bus's own cost per message, from Send to the consumer running, on the in-memory transport (no broker
/// round trip). The Median column is the p50 the requirement names; the target is 5 µs of overhead.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MedianConfig))]
public class MessagingBenchmarks
{
    private ServiceProvider _provider = null!;
    private IHostedService _host = null!;
    private IBus _bus = null!;
    private BenchHandled _handled = null!;
    private Uri _address = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _handled = new BenchHandled();
        var services = new ServiceCollection();
        services.AddSingleton(_handled);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<BenchPingConsumer, BenchPing>("bench-ping");
            b.UseInMemory();
        });
        _provider = services.BuildServiceProvider();
        _host = _provider.GetServices<IHostedService>().Single();
        await _host.StartAsync(CancellationToken.None);
        _bus = _provider.GetRequiredService<IBus>();
        _address = new Uri("queue:bench-ping");
    }

    [Benchmark]
    public async Task SendAndHandle()
    {
        var handled = _handled.Next();
        await _bus.Send(_address, new BenchPing());
        await handled.Task;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _host.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }
}
