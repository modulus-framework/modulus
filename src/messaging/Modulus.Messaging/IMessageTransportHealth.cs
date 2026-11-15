namespace Modulus.Messaging;

/// <summary>
/// Optional transport capability: reports whether the broker or database the transport uses is reachable right now
/// (FR-24). A transport that does not implement it reports healthy, and the health check says so.
/// </summary>
public interface IMessageTransportHealth
{
    /// <summary>Returns true when the transport can send and receive. A check that cannot complete throws.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
