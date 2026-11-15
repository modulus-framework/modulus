namespace Modulus.Messaging.Sagas;

/// <summary>The stored state of one saga instance. Derive it and add the data the saga needs between its events.</summary>
public abstract class SagaInstance
{
    /// <summary>Identifies the instance: every event of one process carries the same key.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Name of the current state. Empty before the first transition.</summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Stored version, set by the repository when the instance is saved. A save or delete of a copy whose version is
    /// no longer the stored one fails (optimistic concurrency, FR-19), so no concurrent update is lost.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Tokens of the timeouts this saga scheduled. A saga that completes cancels them, so a timeout it no longer needs
    /// never reaches it. A token whose message already arrived cancels nothing, which is harmless.
    /// </summary>
    public List<Guid> PendingTimeouts { get; set; } = [];
}
