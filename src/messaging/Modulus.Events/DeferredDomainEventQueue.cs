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
    private int _holds;

    public bool IsDispatchHeld => _holds > 0;

    public void Enqueue(IEnumerable<IDomainEvent> events) => _queue.AddRange(events);

    public IReadOnlyList<IDomainEvent> DequeueAll()
    {
        var copy = _queue.ToList();
        _queue.Clear();
        return copy;
    }

    public IDisposable HoldDispatch()
    {
        _holds++;
        return new Hold(this);
    }

    private sealed class Hold(DeferredDomainEventQueue owner) : IDisposable
    {
        private DeferredDomainEventQueue? _owner = owner;

        public void Dispose()
        {
            var o = Interlocked.Exchange(ref _owner, null);
            if (o is not null)
                o._holds--;
        }
    }
}
