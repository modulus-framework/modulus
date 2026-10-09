namespace Modulus.Realtime;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Realtime.Delivery;

/// <summary>Pushes a message to connected clients, from any code (handlers, background jobs).</summary>
public interface IRealtimePublisher
{
    /// <summary>
    /// Publishes <paramref name="data"/> as event <paramref name="type"/> to <paramref name="audience"/>, in the ambient
    /// tenant. Delivery is best-effort: clients that are not connected (or whose node is unreachable) miss it, apart
    /// from the replay a reconnecting client gets.
    /// </summary>
    Task PublishAsync(string type, object? data, RealtimeAudience audience, CancellationToken ct = default);
}

/// <summary>Carries messages to every node that holds connections.</summary>
public interface IRealtimeBackplane
{
    /// <summary>Sends <paramref name="message"/> to every node (including this one), each of which dispatches it locally.</summary>
    Task PublishAsync(RealtimeMessage message, CancellationToken ct = default);
}

/// <summary>The single-node backplane: dispatches straight to this node's connections.</summary>
internal sealed class InProcessRealtimeBackplane(IRealtimeDispatcher dispatcher) : IRealtimeBackplane
{
    public Task PublishAsync(RealtimeMessage message, CancellationToken ct = default) => dispatcher.DispatchAsync(message, ct);
}

internal sealed class RealtimePublisher(IRealtimeBackplane backplane, IServiceProvider services, TimeProvider clock) : IRealtimePublisher
{
    public Task PublishAsync(string type, object? data, RealtimeAudience audience, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(audience);
        if (audience.Users is { Count: 0 })
            return Task.CompletedTask;

        var message = new RealtimeMessage(
            global::Modulus.GuidV7.Create().ToString("N"),
            type,
            JsonSerializer.Serialize(data, RealtimeMessage.JsonOptions),
            services.GetService<ICurrentTenant>()?.TenantId,
            audience.Users,
            audience.Topic,
            audience.RequiredPermission,
            clock.GetUtcNow());
        return backplane.PublishAsync(message, ct);
    }
}
