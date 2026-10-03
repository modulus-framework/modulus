namespace Modulus.Realtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Realtime.Delivery;

/// <summary>Configures what is pushed to clients (<see cref="RealtimeServiceCollectionExtensions.AddModulusRealtime(IServiceCollection, IConfiguration, Action{RealtimeBuilder}?)"/>).</summary>
public sealed class RealtimeBuilder
{
    internal RealtimeBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Pushes integration event <typeparamref name="TEvent"/> to the clients <paramref name="audience"/> picks, as event
    /// <c>[IntegrationEventName]</c>. The SSE <c>data</c> / SignalR <c>Data</c> is the event itself, or what
    /// <paramref name="payload"/> returns: map it, since everything in it reaches browsers and devices.
    /// </summary>
    /// <example><c>r.AddEvent&lt;ProductCreated&gt;(_ =&gt; RealtimeAudience.Permission("catalog:products:manage"), e =&gt; new { e.Id })</c></example>
    public RealtimeBuilder AddEvent<TEvent>(Func<TEvent, RealtimeAudience> audience, Func<TEvent, object?>? payload = null)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(audience);
        var name = IntegrationEventNaming.GetName(typeof(TEvent));
        if (name.StartsWith(RealtimeEvents.ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Realtime event names starting with '{RealtimeEvents.ReservedPrefix}' are reserved.");

        Services.AddSingleton(new RealtimeEventDescriptor(
            typeof(TEvent),
            name,
            e => audience((TEvent)e),
            payload is null ? null : e => payload((TEvent)e)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, RealtimeEventHandler<TEvent>>());

        // Broker consumers subscribe to the registry's routing keys, so a service that only pushes an event still gets it.
        SharedEventRegistry(Services).Register(typeof(TEvent));
        return this;
    }

    /// <summary>
    /// A topic clients may follow (SSE <c>?topics=</c> at connect, SignalR <c>Subscribe</c> at any time): an exact name or a
    /// prefix ending in <c>*</c>. Following needs <paramref name="permission"/> when set and <paramref name="authorize"/> to
    /// return true when set. A topic that matches no registration is refused.
    /// </summary>
    /// <example><c>r.AddTopic("orders:*", "orders:read", async t =&gt; await IsOwnOrderAsync(t.Services, t.User, t.Key))</c></example>
    public RealtimeBuilder AddTopic(string pattern, string? permission = null, Func<RealtimeTopicContext, ValueTask<bool>>? authorize = null)
    {
        RealtimeTopics.ValidatePattern(pattern);
        if (permission is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Services.AddSingleton(new RealtimeTopicDefinition(pattern, permission, authorize));
        return this;
    }

    private static IntegrationEventRegistry SharedEventRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(IIntegrationEventRegistry)
                && d.ImplementationInstance is IntegrationEventRegistry)?.ImplementationInstance is IntegrationEventRegistry existing)
            return existing;

        var registry = new IntegrationEventRegistry();
        services.AddSingleton<IIntegrationEventRegistry>(registry);
        return registry;
    }
}

/// <summary>Events the framework itself sends.</summary>
public static class RealtimeEvents
{
    /// <summary>Names starting with this are the framework's own.</summary>
    public const string ReservedPrefix = "modulus.";

    /// <summary>First SSE event of a stream: <c>{"connectionId"}</c>. Messages published after it reach the client.</summary>
    public const string Ready = "modulus.ready";

    /// <summary>The <c>Last-Event-ID</c> is no longer replayable: messages were missed, so reload the state.</summary>
    public const string Reset = "modulus.reset";
}

/// <summary>Registration of <c>Modulus.Realtime</c>.</summary>
public static class RealtimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers realtime delivery (settings <c>Realtime</c>): <see cref="IRealtimePublisher"/>, the single-node backplane
    /// (replace it with a shared one, e.g. <c>AddRedisRealtimeBackplane</c>, when the host runs more than one replica) and
    /// SignalR when <c>Realtime:SignalR:Enabled</c>. Map the endpoints with <c>MapModulusRealtime()</c>.
    /// </summary>
    public static IServiceCollection AddModulusRealtime(this IServiceCollection services, IConfiguration configuration, Action<RealtimeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!services.Any(d => d.ServiceType == typeof(RealtimeMarker)))
        {
            services.AddSingleton<RealtimeMarker>();
            services.AddOptions<ModulusRealtimeOptions>()
                .Bind(configuration.GetSection(ModulusRealtimeOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(o => o.Path.StartsWith('/'), "Realtime:Path must start with '/'.")
                .Validate(o => o.Sse.HeartbeatInterval > TimeSpan.Zero, "Realtime:Sse:HeartbeatInterval must be positive.")
                .Validate(o => o.PermissionRecheckInterval >= TimeSpan.Zero, "Realtime:PermissionRecheckInterval must not be negative.")
                .ValidateOnStart();

            services.TryAddSingleton(TimeProvider.System);
            services.AddMetrics();
            services.TryAddSingleton<RealtimeMetrics>();
            services.TryAddSingleton<RealtimeDispatcher>();
            services.TryAddSingleton<IRealtimeDispatcher>(sp => sp.GetRequiredService<RealtimeDispatcher>());
            services.TryAddSingleton<IRealtimeBackplane, InProcessRealtimeBackplane>();
            services.TryAddScoped<IRealtimePublisher, RealtimePublisher>();
            services.TryAddSingleton<IRealtimeTopicAuthorizer, RealtimeTopicAuthorizer>();
            services.AddAuthorization();

            var signalR = configuration.GetSection(ModulusRealtimeOptions.SectionName).Get<ModulusRealtimeOptions>()?.SignalR.Enabled ?? false;
            if (signalR)
                services.AddSignalR();
        }

        configure?.Invoke(new RealtimeBuilder(services));
        return services;
    }

    internal sealed class RealtimeMarker;
}
