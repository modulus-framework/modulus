using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging;
using Modulus.Messaging.Transports.InMemory;

namespace Modulus.Benchmarks;

/// <summary>A pass-through filter (a registered filter runs for every send, publish and consume).</summary>
public sealed class PublishOnlyFilter : IMessageFilter
{
    public Task Execute(MessageFilterContext context, Func<Task> next) => next();
}

/// <summary>An observer that listens to publishes only, so the other events have nothing to call.</summary>
public sealed class PublishOnlyObserver : IMessageObserver
{
    public Task OnPublished(MessageEnvelope envelope) => Task.CompletedTask;
}

/// <summary>
/// FR-C4: a feature that is not registered costs nothing per message, and an observer that does not listen to the event
/// at hand costs little. The same send-and-handle as NFR-06, with nothing, with an observer that only listens to publishes,
/// and with a filter (filters run at every point, so a registered filter is a feature in use). Compare the Mean columns.
/// </summary>
[MemoryDiagnoser]
public class MessagingUnusedFeaturesBenchmarks
{
    /// <summary>None: nothing registered. Observer: an observer that only listens to publishes. Filter: a filter that runs at every point.</summary>
    [ParamsAllValues]
    public FeatureSet Features { get; set; }

    public enum FeatureSet
    {
        None,
        Observer,
        Filter,
    }

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
            b.AddConsumer<BenchPingConsumer, BenchPing>("bench-ping-unused");
            if (Features == FeatureSet.Observer)
            {
                b.AddObserver<PublishOnlyObserver>();
            }

            if (Features == FeatureSet.Filter)
            {
                b.AddFilter<PublishOnlyFilter>();
            }

            b.UseInMemory();
        });
        _provider = services.BuildServiceProvider();
        _host = _provider.GetServices<IHostedService>().Single();
        await _host.StartAsync(CancellationToken.None);
        _bus = _provider.GetRequiredService<IBus>();
        _address = new Uri("queue:bench-ping-unused");
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
