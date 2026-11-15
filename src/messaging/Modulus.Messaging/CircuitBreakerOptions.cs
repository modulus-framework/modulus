namespace Modulus.Messaging;

/// <summary>
/// Stops an endpoint while its consumer keeps failing (optional, FR-15). After <see cref="FailureThreshold"/> failures in
/// a row the circuit opens and no message is started for <see cref="OpenDuration"/>. Then one trial message is let through:
/// if it succeeds the circuit closes; if it fails the circuit opens again for another <see cref="OpenDuration"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="KillSwitchOptions"/>, which restarts with an empty window, the breaker tests the consumer with a real
/// message before it resumes. Outcomes of messages admitted before the circuit opened decide the state too, so the first
/// outcome after the trial is the decision.
/// </remarks>
public sealed class CircuitBreakerOptions
{
    /// <summary>Consecutive failures that open the circuit.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long the circuit stays open before a trial message is let through.</summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (FailureThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FailureThreshold), "Must be positive.");
        }

        if (OpenDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenDuration), "Must be positive.");
        }
    }

    internal CircuitBreakerOptions Clone() => new()
    {
        FailureThreshold = FailureThreshold,
        OpenDuration = OpenDuration,
    };
}
