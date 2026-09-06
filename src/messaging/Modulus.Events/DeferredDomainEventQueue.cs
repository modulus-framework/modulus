namespace Modulus.Events;

using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

/// <summary>
/// Scoped queue for domain events that defer dispatch until after an explicit
/// transaction commits, ensuring the documented "after commit" semantics hold.
/// </summary>
public sealed class DeferredDomainEventQueue : IDeferredDomainEventQueue
{
    private readonly List<IDomainEvent> _queue = [];

    public void Enqueue(IEnumerable<IDomainEvent> events) => _queue.AddRange(events);

    public IReadOnlyList<IDomainEvent> DequeueAll()
    {
        var copy = _queue.ToList();
        _queue.Clear();
        return copy;
    }
}
