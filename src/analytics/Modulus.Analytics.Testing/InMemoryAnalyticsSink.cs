namespace Modulus.Analytics.Testing;

using System.Collections.Concurrent;

/// <summary>
/// A sink that keeps rows in memory, one per (tenant, dataset, event id), the way a deduplicating warehouse table does.
/// Use it in tests, and as the reference behaviour providers are checked against.
/// </summary>
public sealed class InMemoryAnalyticsSink : IAnalyticsErasableSink
{
    private readonly ConcurrentDictionary<(Guid Tenant, string Dataset, Guid EventId), AnalyticsRecord> _rows = new();
    private readonly ConcurrentDictionary<string, AnalyticsDatasetDescriptor> _datasets = new(StringComparer.Ordinal);

    /// <summary>Creates a sink with the given <paramref name="name"/>.</summary>
    /// <param name="name">The sink name. Defaults to <c>memory</c>.</param>
    public InMemoryAnalyticsSink(string name = "memory") => Name = name;

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>How many times <see cref="EnsureDatasetAsync"/> was called.</summary>
    public int EnsureCalls { get; private set; }

    /// <summary>When set, <see cref="WriteAsync"/> throws it. Lets a test simulate a failing provider.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>The datasets set up so far, by key.</summary>
    public IReadOnlyDictionary<string, AnalyticsDatasetDescriptor> Datasets => _datasets;

    /// <summary>Every stored row.</summary>
    public IReadOnlyCollection<AnalyticsRecord> Rows => [.. _rows.Values];

    /// <inheritdoc />
    public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken)
    {
        EnsureCalls++;
        _datasets[dataset.Key] = dataset;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> CountAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
        => Task.FromResult((long)_rows.Keys.Count(k => k.Tenant == tenantId && k.Dataset == dataset.Name));

    /// <inheritdoc />
    public Task<long> CountTenantAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
        => CountAsync(dataset, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<long> EraseTenantAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
    {
        long removed = 0;
        foreach (var key in _rows.Keys.Where(k => k.Tenant == tenantId && k.Dataset == dataset.Name).ToArray())
        {
            if (_rows.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        foreach (var record in records)
        {
            _rows[(record.TenantId, record.Dataset, record.EventId)] = record;
        }

        return Task.CompletedTask;
    }
}
