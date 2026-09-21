namespace Modulus.Testing.Fakes;

using Modulus.Events.Abstractions;

/// <summary>
/// Test double for <see cref="IModuleBus"/> that records all published events
/// for assertion in tests. Use <see cref="PublishedEvents{TEvent}"/> to retrieve them.
/// </summary>
public sealed class RecordingModuleBus : IModuleBus
{
    // Lock-guarded: handlers publish concurrently in tests; an unsynchronised
    // List corrupts (lost entries, torn state) under concurrent Add.
    private readonly Lock _gate = new();
    private readonly List<IIntegrationEvent> _published = [];

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        lock (_gate)
            _published.Add(@event);
        await Task.CompletedTask;
    }

    /// <summary>Returns all published events of type TEvent.</summary>
    public IReadOnlyList<TEvent> PublishedEvents<TEvent>()
        where TEvent : IIntegrationEvent
    {
        lock (_gate)
            return _published.OfType<TEvent>().ToList().AsReadOnly();
    }

    /// <summary>Returns all published events (of any type).</summary>
    public IReadOnlyList<IIntegrationEvent> AllPublishedEvents
    {
        get
        {
            lock (_gate)
                return _published.ToList().AsReadOnly();
        }
    }

    /// <summary>Clears the publication history.</summary>
    public void Clear()
    {
        lock (_gate)
            _published.Clear();
    }
}
