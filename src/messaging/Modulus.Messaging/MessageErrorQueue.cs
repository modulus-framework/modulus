namespace Modulus.Messaging;

/// <summary>
/// Moves parked messages (<c>_error</c>) back to the endpoint they came from (FR-09). A replayed message is delivered
/// like a new one: it runs its consumer again, with its retries and redeliveries reset.
/// </summary>
public interface IMessageErrorQueue
{
    /// <summary>Replays up to <paramref name="limit"/> messages of the endpoint's <c>_error</c> queue. Returns how many were moved.</summary>
    Task<int> ReplayAsync(string endpoint, int limit = int.MaxValue, CancellationToken cancellationToken = default);
}

/// <summary>A transport that can move the messages of an endpoint's <c>_error</c> queue back to the endpoint (FR-09).</summary>
public interface IErrorReplayTransport
{
    /// <summary>Moves up to <paramref name="limit"/> messages from <c>endpoint_error</c> to <c>endpoint</c>, oldest first.</summary>
    Task<int> ReplayErrors(string endpoint, int limit, CancellationToken cancellationToken);
}
