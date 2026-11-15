namespace Modulus.Messaging;

/// <summary>
/// Pauses an endpoint when too many of its recent messages failed (FR-15). The endpoint stops taking messages
/// for <see cref="RestartTimeout"/>, then restarts with an empty window.
/// </summary>
public sealed class KillSwitchOptions
{
    /// <summary>Failed share of the window that trips the switch, from 0 (exclusive) to 1 (inclusive).</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>Outcomes kept for the failure ratio. The switch cannot trip until this many have been seen.</summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>How long the endpoint stays paused after the switch trips.</summary>
    public TimeSpan RestartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (!(FailureRatio > 0 && FailureRatio <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(FailureRatio), "Must be greater than 0 and at most 1.");
        }

        if (WindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WindowSize), "Must be positive.");
        }

        if (RestartTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RestartTimeout), "Must not be negative.");
        }
    }

    internal KillSwitchOptions Clone() => new()
    {
        FailureRatio = FailureRatio,
        WindowSize = WindowSize,
        RestartTimeout = RestartTimeout,
    };
}
