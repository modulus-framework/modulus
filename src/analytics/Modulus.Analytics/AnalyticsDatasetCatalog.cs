namespace Modulus.Analytics;

/// <summary>The registered analytics datasets, every version of each.</summary>
public interface IAnalyticsDatasetCatalog
{
    /// <summary>Every registered dataset version, ordered by name and then version.</summary>
    IReadOnlyList<AnalyticsDatasetDescriptor> All { get; }

    /// <summary>Finds one dataset version.</summary>
    /// <param name="name">The dataset name.</param>
    /// <param name="version">The version.</param>
    /// <param name="dataset">The descriptor when found.</param>
    bool TryGet(string name, int version, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AnalyticsDatasetDescriptor? dataset);

    /// <summary>The highest registered version of <paramref name="name"/>, or <see langword="null"/>.</summary>
    /// <param name="name">The dataset name.</param>
    AnalyticsDatasetDescriptor? Latest(string name);
}

/// <summary>The default, in-memory <see cref="IAnalyticsDatasetCatalog"/>, filled at startup from <see cref="AnalyticsBuilder"/>.</summary>
public sealed class AnalyticsDatasetCatalog : IAnalyticsDatasetCatalog
{
    private readonly SortedDictionary<(string Name, int Version), AnalyticsDatasetDescriptor> _datasets = [];

    /// <inheritdoc />
    public IReadOnlyList<AnalyticsDatasetDescriptor> All => [.. _datasets.Values];

    /// <summary>Adds a dataset version after validating it.</summary>
    /// <param name="dataset">The descriptor.</param>
    /// <exception cref="InvalidOperationException">The descriptor is invalid, or that name and version is already registered.</exception>
    public void Add(AnalyticsDatasetDescriptor dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var problems = dataset.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Analytics dataset '{dataset.Key}' is invalid: {string.Join(" ", problems)}");
        }

        if (!_datasets.TryAdd((dataset.Name, dataset.Version), dataset))
        {
            throw new InvalidOperationException($"Analytics dataset '{dataset.Key}' is registered twice.");
        }
    }

    /// <inheritdoc />
    public bool TryGet(string name, int version, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AnalyticsDatasetDescriptor? dataset)
        => _datasets.TryGetValue((name, version), out dataset);

    /// <inheritdoc />
    public AnalyticsDatasetDescriptor? Latest(string name)
        => _datasets.Where(d => d.Key.Name == name).Select(d => d.Value).LastOrDefault();
}
