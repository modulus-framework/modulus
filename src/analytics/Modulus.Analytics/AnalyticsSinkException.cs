namespace Modulus.Analytics;

/// <summary>A sink could not complete a call.</summary>
public sealed class AnalyticsSinkException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="isTransient"><see langword="true"/> when a later attempt may succeed (timeout, throttling); <see langword="false"/> for a rejection that will repeat (bad schema, bad credentials).</param>
    /// <param name="innerException">The underlying error.</param>
    public AnalyticsSinkException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException) => IsTransient = isTransient;

    /// <summary>Whether a later attempt may succeed. A non-transient failure goes straight to the dead letters.</summary>
    public bool IsTransient { get; }
}
