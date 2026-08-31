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
        _registry = registry;
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
            .Cast<object>()
            .ToList();

        if (handlers.Count == 0)
            return false;

        // One compiled delegate per closed handler interface, cached process-wide
        // — reflection (GetMethod + Invoke) runs only on the first dispatch.
        var invoker = s_handlerInvokers.GetOrAdd(handlerType, CompileHandlerInvoker);
        foreach (var handler in handlers)
            await invoker(handler, @event, ct);

        return true;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,
        Func<object, object, CancellationToken, Task>> s_handlerInvokers = new();

    private static Func<object, object, CancellationToken, Task> CompileHandlerInvoker(Type handlerType)
    {
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
        var handlerParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "handler");
        var eventParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "event");
        var ctParam = System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken), "ct");
        var eventType = method.GetParameters()[0].ParameterType;
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Convert(handlerParam, handlerType),
            method,
            System.Linq.Expressions.Expression.Convert(eventParam, eventType),
            ctParam);
        return System.Linq.Expressions.Expression.Lambda<Func<object, object, CancellationToken, Task>>(
            call, handlerParam, eventParam, ctParam).Compile();
    }
}
