namespace Modulus.Core.Abstractions;

/// <summary>
/// Ambient accessor for the causation id — the ID of the integration event that caused
/// the current operation. Set when a message consumer handles an event and publishes
/// downstream events. Null when the operation originated from an HTTP request or
/// background job.
/// </summary>
/// <remarks>
/// Implemented by <c>Modulus.Core.Correlation.CausationIdContext</c> over a
/// static <see cref="System.Threading.AsyncLocal{T}"/>, mirroring <see cref="ICorrelationContext"/>.
/// Message consumers (RabbitMQ, Kafka, outbox relay) establish it from the
/// envelope's EventId, so every event published during handling carries forward
/// the chain.
/// </remarks>
public interface ICausationIdContext
{
    /// <summary>The current causation id (the ID of the event that caused this operation), or null when none is set.</summary>
    string? CausationId { get; }

    /// <summary>Whether a causation id is in scope for the current async flow.</summary>
    bool IsSet { get; }

    /// <summary>
    /// Establishes <paramref name="causationId"/> as the ambient causation id
    /// for the current async flow and returns a scope that restores the previous
    /// value when disposed.
    /// </summary>
    IDisposable BeginScope(string causationId);
}
