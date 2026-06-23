namespace Modulus.Events;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;

/// <summary>
/// Resolves an incoming <see cref="IntegrationEventEnvelope"/> to its CLR type,
/// deserialises the payload, and dispatches to all registered
/// <see cref="IIntegrationEventHandler{TEvent}"/> instances within a DI scope.
/// </summary>
public sealed class IntegrationEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventRegistry _registry;

    public IntegrationEventDispatcher(
        IServiceScopeFactory scopeFactory,
        IIntegrationEventRegistry registry)
    {
        _scopeFactory = scopeFactory;
        _registry     = registry;
    }

    /// <summary>
    /// Deserialises and dispatches the envelope.
    /// Returns <c>false</c> when the event type is unknown (no handler registered).
    /// </summary>
    public async Task<bool> DispatchAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken ct = default)
    {
        if (!_registry.TryGetType(envelope.RoutingKey, out var eventType))
            return false;

        var @event = JsonSerializer.Deserialize(envelope.Payload, eventType);
        if (@event is null)
            return false;

        using var scope = _scopeFactory.CreateScope();
        var handlerType = typeof(IIntegrationEventHandler<>)
            .MakeGenericType(eventType);

        var handlers = scope.ServiceProvider
            .GetServices(handlerType)
            .Where(h => h is not null)
            .ToList();

        if (handlers.Count == 0)
            return false;

        foreach (var handler in handlers)
        {
            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
            var task = (Task)method.Invoke(handler, [@event, ct])!;
            await task;
        }

        return true;
    }
}
