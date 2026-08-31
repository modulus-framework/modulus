namespace Modulus.Testing.Assertions;

using Modulus.Events.Abstractions;
using Modulus.Testing.Fakes;

/// <summary>
/// Assertion helpers for <see cref="RecordingModuleBus"/> published events.
/// </summary>
public static class EventAssertions
{
    /// <summary>
    /// Returns all published events of type TEvent.
    /// </summary>
    public static IReadOnlyList<TEvent> GetPublishedEvents<TEvent>(this RecordingModuleBus bus)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.PublishedEvents<TEvent>();
    }

    /// <summary>
    /// Checks if any event of type TEvent was published matching the predicate.
    /// </summary>
    public static bool HasPublished<TEvent>(
        this RecordingModuleBus bus,
        Func<TEvent, bool> predicate)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(predicate);
        return bus.PublishedEvents<TEvent>().Any(predicate);
    }

    /// <summary>
    /// Counts events of type TEvent matching the predicate.
    /// </summary>
    public static int CountPublished<TEvent>(
        this RecordingModuleBus bus,
        Func<TEvent, bool>? predicate = null)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(bus);
        var events = bus.PublishedEvents<TEvent>();
        return predicate is null ? events.Count : events.Count(predicate);
    }
}
