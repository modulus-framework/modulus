namespace Modulus.Events;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

/// <summary>
/// Called by ModuleDbContext.SaveChangesAsync after transaction commits.
/// Dispatches each collected domain event to all registered handlers via
/// compiled expression-tree delegates — no DLR/dynamic overhead on the hot path.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider sp)
{
    // Cache: (handlerType, eventType) → compiled delegate
    private static readonly ConcurrentDictionary<(Type, Type), Func<object, IDomainEvent, CancellationToken, Task>>
        s_delegates = new();

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> events,
        CancellationToken ct = default)
    {
        var errors = new List<Exception>();
        foreach (var @event in events)
        {
            var handlerType = typeof(IDomainEventHandler<>)
                .MakeGenericType(@event.GetType());

            var handlers = sp.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                if (handler is null) continue;
                try
                {
                    var invoke = s_delegates.GetOrAdd(
                        (handler.GetType(), @event.GetType()),
                        static key => CompileDelegate(key.Item1, key.Item2));
                    await invoke(handler, @event, ct);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw errors.Count == 1
                ? errors[0]
                : new AggregateException("One or more domain event handlers failed.", errors);
        }
    }

    private static Func<object, IDomainEvent, CancellationToken, Task> CompileDelegate(
        Type handlerType, Type eventType)
    {
        var method = handlerType.GetMethod(
            "HandleAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [eventType, typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException(
                $"Handler '{handlerType.FullName}' has no HandleAsync({eventType.Name}, CancellationToken) method.");

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var eventParam = Expression.Parameter(typeof(IDomainEvent), "event");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var castHandler = Expression.Convert(handlerParam, handlerType);
        var castEvent = Expression.Convert(eventParam, eventType);
        var call = Expression.Call(castHandler, method, castEvent, ctParam);

        return Expression.Lambda<Func<object, IDomainEvent, CancellationToken, Task>>(
            call, handlerParam, eventParam, ctParam).Compile();
    }
}
