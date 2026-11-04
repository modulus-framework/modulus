namespace Modulus.Analytics;

/// <summary>
/// A destination for analytics data (a warehouse, a file store, a broker). Business modules never see it: they publish events and
/// the delivery pipeline calls the sinks, so a provider can be replaced without touching a module.
/// </summary>
public interface IAnalyticsSink
{
    /// <summary>A short, stable name used in configuration, metrics and the management API, for example <c>clickhouse</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Creates or updates the provider-side structure for <paramref name="dataset"/> (a table, a schema file, a topic).
    /// It must be safe to call repeatedly with the same descriptor.
    /// </summary>
    /// <param name="dataset">The dataset contract.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken);

    /// <summary>
    /// Writes already-masked <paramref name="records"/>. Writing a record that was written before must not create a duplicate
    /// (it is a no-op or an overwrite), because delivery is at least once. Values carry their declared CLR types: string, long,
    /// decimal, double, bool, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> and <see cref="Guid"/>; a hashed field is a string.
    /// </summary>
    /// <param name="dataset">The dataset the records belong to.</param>
    /// <param name="records">The masked rows. All belong to <paramref name="dataset"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="AnalyticsSinkException">The write failed; <see cref="AnalyticsSinkException.IsTransient"/> says whether to retry.</exception>
    Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken);
}
