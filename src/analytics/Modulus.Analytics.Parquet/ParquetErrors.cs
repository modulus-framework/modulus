namespace Modulus.Analytics.Parquet;

/// <summary>Decides whether a failure writing a file is worth retrying.</summary>
internal static class ParquetErrors
{
    internal static AnalyticsSinkException Translate(string action, Exception exception) => exception switch
    {
        AnalyticsSinkException known => known,
        // A bad value, an unsupported type or a path the storage refuses repeats on every attempt.
        ArgumentException or InvalidCastException or FormatException or OverflowException or NotSupportedException or InvalidOperationException =>
            new AnalyticsSinkException($"Could not {action}: {exception.Message}", isTransient: false, exception),
        // Network, disk and storage-service errors are usually temporary.
        _ => new AnalyticsSinkException($"Failed to {action}: {exception.Message}", isTransient: true, exception),
    };
}
