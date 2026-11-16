namespace Modulus.Messaging.Transports.RabbitMQ;

/// <summary>The wait before a reconnect attempt: doubles from the minimum with each failed attempt, never above the maximum (NFR-02).</summary>
internal static class RabbitMqReconnectBackoff
{
    /// <summary>The wait after failed attempt <paramref name="attempt"/>.</summary>
    /// <param name="attempt">1 for the first failed attempt, 2 for the second, and so on.</param>
    /// <param name="minimum">The wait after the first failed attempt.</param>
    /// <param name="maximum">The longest wait.</param>
    public static TimeSpan DelayFor(int attempt, TimeSpan minimum, TimeSpan maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        // Clamp the exponent: 2^30 already exceeds any sensible maximum, and it keeps the arithmetic finite.
        var milliseconds = minimum.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 30));
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, maximum.TotalMilliseconds));
    }
}
