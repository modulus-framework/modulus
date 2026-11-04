namespace Modulus.Analytics.OpenLineage;

/// <summary>Settings of the OpenLineage emitter (section <c>Analytics:OpenLineage</c>).</summary>
public sealed class OpenLineageOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:OpenLineage";

    /// <summary>Whether backfill runs are reported. Off by default: nothing is sent until a lineage backend is configured.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The OpenLineage endpoint that accepts run events, for example <c>http://marquez:5000/api/v1/lineage</c>. Required when <see cref="Enabled"/> is set.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>The namespace of the operational source datasets and of the job (for example <c>orders-service</c>).</summary>
    public string Namespace { get; set; } = "modulus";

    /// <summary>The namespace of the warehouse tables. Each table is named <c>{sink}/{dataset}_v{version}</c>.</summary>
    public string WarehouseNamespace { get; set; } = "modulus-warehouse";

    /// <summary>The producer URI written into every event, identifying this emitter.</summary>
    public string Producer { get; set; } = "https://github.com/modulus-framework/modulus";

    /// <summary>A bearer token for the lineage endpoint, if it needs one. Keep it in a secret store, not in a committed file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one event may take to be accepted.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
