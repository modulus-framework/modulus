namespace Modulus.Analytics;

/// <summary>A data-quality finding for one tenant, dataset and sink, as the quality check produced it.</summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="TenantId">The tenant whose rows were judged.</param>
/// <param name="Sink">The sink that was profiled.</param>
/// <param name="Finding">The rule that was broken.</param>
public sealed record AnalyticsQualityIssue(string Dataset, int Version, Guid TenantId, string Sink, AnalyticsQualityFinding Finding);

/// <summary>
/// Receives the findings of each data-quality check, to route them to alerting, a ticket queue or a governance tool. Register one or more
/// implementations; each finding goes to all of them. A sink that throws does not stop the check: the error is reported with the check's errors.
/// </summary>
public interface IAnalyticsQualityFindingSink
{
    /// <summary>Records one finding.</summary>
    /// <param name="issue">The finding and where it was found.</param>
    void Record(AnalyticsQualityIssue issue);
}
