namespace Modulus.Realtime;

/// <summary>
/// Delivers a message to this node's connections. A backplane calls it on every node for every message (the in-process
/// backplane calls it directly).
/// </summary>
public interface IRealtimeDispatcher
{
    /// <summary>Records <paramref name="message"/> for replay and queues it for each local connection allowed to see it.</summary>
    Task DispatchAsync(RealtimeMessage message, CancellationToken ct = default);
}
