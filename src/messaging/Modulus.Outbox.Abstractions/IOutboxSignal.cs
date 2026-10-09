namespace Modulus.Outbox.Abstractions;

/// <summary>
/// Wakes this node's outbox poller as soon as new rows are committed, so a dispatch does not wait out the
/// polling interval. A missed or dropped signal costs nothing: the poller still wakes on its interval.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Tells the poller that committed outbox rows are waiting. Never blocks or throws.</summary>
    void Notify();
}
