namespace Modulus.Analytics;

/// <summary>
/// What a sink counts about one company's rows of a dataset version, for the data-quality checks. It holds counts only: no record value leaves the sink.
/// </summary>
/// <param name="RowCount">The distinct records (one per record id).</param>
/// <param name="FutureRows">The records whose occurrence time is after the time the profile was taken, allowing for clock skew.</param>
/// <param name="NullRows">The records with no value, for each nullable field that declares a <see cref="AnalyticsFieldDescriptor.MaxNullRate"/>.</param>
public sealed record AnalyticsWarehouseProfile(long RowCount, long FutureRows, IReadOnlyDictionary<string, long> NullRows);

/// <summary>One data-quality rule a dataset breaks in a sink.</summary>
/// <param name="Check">The rule: <see cref="AnalyticsQualityChecks"/>.</param>
/// <param name="Field">The field the rule is about, or <see langword="null"/> for a dataset-wide rule.</param>
/// <param name="Observed">The value found: a share between 0 and 1, or a count.</param>
/// <param name="Threshold">The limit the value was compared with, when the rule has one.</param>
/// <param name="Message">A sentence for people reading the report.</param>
public sealed record AnalyticsQualityFinding(string Check, string? Field, double Observed, double? Threshold, string Message);

/// <summary>The data-quality results of one company in one sink.</summary>
public sealed record AnalyticsQualityResult(
    string Dataset,
    int Version,
    Guid TenantId,
    string Sink,
    long RowCount,
    IReadOnlyList<AnalyticsQualityFinding> Findings);

/// <summary>The outcome of a data-quality check: one result per company and sink, and the companies or sinks that could not be checked.</summary>
public sealed record AnalyticsQualityReport(IReadOnlyList<AnalyticsQualityResult> Results, IReadOnlyList<string> Errors)
{
    /// <summary>True when no rule was broken and nothing failed to check.</summary>
    public bool IsClean => Errors.Count == 0 && Results.All(r => r.Findings.Count == 0);
}

/// <summary>The data-quality rules and their names.</summary>
public static class AnalyticsQualityChecks
{
    /// <summary>A nullable field has a larger share of nulls than its <see cref="AnalyticsFieldDescriptor.MaxNullRate"/>.</summary>
    public const string NullRateExceeded = "null_rate_exceeded";

    /// <summary>Records are timestamped after the time they were checked, beyond the allowed clock skew: a wrong clock or a broken mapping.</summary>
    public const string FutureOccurrence = "future_occurrence";

    /// <summary>How far ahead of the check a record's occurrence time may be before it counts as a future record.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
}

/// <summary>Turns a sink's profile of a dataset version into findings.</summary>
public static class AnalyticsQuality
{
    /// <summary>Evaluates the rules of <paramref name="dataset"/> against one profile. Pure: the same profile always gives the same findings.</summary>
    public static IReadOnlyList<AnalyticsQualityFinding> Evaluate(AnalyticsDatasetDescriptor dataset, AnalyticsWarehouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(profile);

        var findings = new List<AnalyticsQualityFinding>();
        if (profile.FutureRows > 0)
        {
            findings.Add(new AnalyticsQualityFinding(
                AnalyticsQualityChecks.FutureOccurrence, null, profile.FutureRows, null,
                $"{profile.FutureRows} record(s) are timestamped more than {AnalyticsQualityChecks.ClockSkew.TotalMinutes} minutes in the future."));
        }

        if (profile.RowCount > 0)
        {
            foreach (var field in dataset.Fields.Where(f => f.MaxNullRate is not null))
            {
                var nulls = profile.NullRows.TryGetValue(field.Name, out var count) ? count : 0;
                var rate = (double)nulls / profile.RowCount;
                if (rate > field.MaxNullRate!.Value)
                {
                    findings.Add(new AnalyticsQualityFinding(
                        AnalyticsQualityChecks.NullRateExceeded, field.Name, rate, field.MaxNullRate,
                        $"{rate:P1} of the '{field.Name}' values are null; the limit is {field.MaxNullRate.Value:P1}."));
                }
            }
        }

        return findings;
    }
}
