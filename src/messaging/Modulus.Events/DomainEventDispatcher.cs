namespace Modulus.Events;

using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

/// <summary>
/// Called by ModuleDbContext.SaveChangesAsync after transaction commits.
/// Dispatches each collected domain event to all registered handlers.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider sp)
{
    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> events,
        CancellationToken ct = default)
    {
        foreach (var @event in events)
        {
            var handlerType = typeof(IDomainEventHandler<>)
                .MakeGenericType(@event.GetType());

            var handlers = sp.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                if (handler is null) continue;
                await ((dynamic)handler).HandleAsync((dynamic)@event, ct);
            }
        }
    }
}