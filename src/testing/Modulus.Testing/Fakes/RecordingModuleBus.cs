namespace Modulus.Testing.Fakes;

using Modulus.Events.Abstractions;

/// <summary>
/// Test double for <see cref="IModuleBus"/> that records all published events
/// for assertion in tests. Use <see cref="PublishedEvents{TEvent}"/> to retrieve them.
/// </summary>
public sealed class RecordingModuleBus : IModuleBus
{
    private readonly List<IIntegrationEvent> _published = [];

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        _published.Add(@event);
        await Task.CompletedTask;
    }

    /// <summary>Returns all published events of type TEvent.</summary>
    public IReadOnlyList<TEvent> PublishedEvents<TEvent>()
        where TEvent : IIntegrationEvent
        => _published.OfType<TEvent>().ToList().AsReadOnly();

    /// <summary>Returns all published events (of any type).</summary>
    public IReadOnlyList<IIntegrationEvent> AllPublishedEvents => _published.AsReadOnly();

    /// <summary>Clears the publication history.</summary>
    public void Clear() => _published.Clear();
}
