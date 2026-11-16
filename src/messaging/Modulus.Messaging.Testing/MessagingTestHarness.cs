using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Transports.InMemory;

namespace Modulus.Messaging.Testing;

/// <summary>
/// Builds a bus on the in-memory transport, starts it, and stops it on dispose.
/// Configure consumers in the callback exactly as in production code.
/// </summary>
public sealed class MessagingTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHostedService _host;

    private MessagingTestHarness(ServiceProvider provider, IHostedService host, InMemoryTransport transport, FaultInjector faults, VirtualClock? clock)
    {
        _provider = provider;
        _host = host;
        Bus = provider.GetRequiredService<IBus>();
        Transport = transport;
        Faults = faults;
        Clock = clock;
    }

    public IBus Bus { get; }

    /// <summary>Makes consumers fail on purpose (FR-25).</summary>
    public FaultInjector Faults { get; }

    /// <summary>The virtual clock, when the harness was started with one; null on the real clock.</summary>
    public VirtualClock? Clock { get; }

    /// <summary>The transport, for inspecting the <c>_error</c> and <c>_skipped</c> queues.</summary>
    public InMemoryTransport Transport { get; }

    /// <summary>Services of the host; consumers resolve from scopes created from it.</summary>
    public IServiceProvider Services => _provider;

    public static Task<MessagingTestHarness> StartAsync(Action<MessagingBusBuilder> configure, Action<IServiceCollection>? services = null) =>
        StartAsync(configure, services, clock: null);

    /// <summary>Starts the harness on a virtual clock: the bus's delays and timestamps follow <paramref name="clock"/>.</summary>
    public static Task<MessagingTestHarness> StartAsync(Action<MessagingBusBuilder> configure, Action<IServiceCollection>? services, VirtualClock? clock) =>
        StartCoreAsync(configure, services, clock);

    private static async Task<MessagingTestHarness> StartCoreAsync(Action<MessagingBusBuilder> configure, Action<IServiceCollection>? services, VirtualClock? clock)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var transport = clock is null ? new InMemoryTransport() : new InMemoryTransport(InMemoryTransport.DefaultStopTimeout, clock);
        var faults = new FaultInjector();
        var collection = new ServiceCollection();
        if (clock is not null)
        {
            collection.AddSingleton<TimeProvider>(clock);
        }

        collection.AddSingleton(faults);
        services?.Invoke(collection);
        collection.AddModulusMessaging(builder =>
        {
            configure(builder);
            builder.AddFilter<FaultInjector>();
            builder.UseTransport(_ => transport);
        });

        var provider = collection.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return new MessagingTestHarness(provider, host, transport, faults, clock);
    }

    /// <summary>Polls until the condition holds or the timeout passes. Returns whether it held.</summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>Stops receiving the way a host shutdown does: in-flight messages finish first.</summary>
    public Task StopAsync() => _host.StopAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
