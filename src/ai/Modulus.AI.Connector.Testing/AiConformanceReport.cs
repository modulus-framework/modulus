namespace Modulus.AI.Connector.Testing;

using System.Text;

/// <summary>
/// What a group of checks protects, after the platform's certification categories (Architecture §9.3, Integration
/// Guide §14.1).
/// </summary>
public enum AiConformanceCategory
{
    /// <summary><c>GET /health</c> answers with the contract version.</summary>
    Health,

    /// <summary>API keys and the signed envelope are checked on every call.</summary>
    Authentication,

    /// <summary>No call reaches another tenant's or app instance's data.</summary>
    TenantIsolation,

    /// <summary>The declared capabilities, resource types and classifications are well-formed and match the answers.</summary>
    Manifest,

    /// <summary>Unknown or malformed requests get typed errors, never partial success or a crash.</summary>
    DenyPaths,

    /// <summary>Undeclared argument names are refused and query syntax in values never breaks a call (FR-26a).</summary>
    QueryInjection,

    /// <summary>Fields outside the user's authorized set never appear, and the field checks agree with the scope.</summary>
    FieldSecurity,

    /// <summary>Batch resource checks answer each record, in order, with partial denies.</summary>
    Authorization,

    /// <summary><c>/extract</c> and <c>/changes</c> resume from their cursors, and tombstones are well-formed.</summary>
    Extraction,

    /// <summary>An access change makes the app call <c>/revocations/scope</c>, retried with the same payload (AD-12).</summary>
    Revocation,

    /// <summary>A permission change shows on the next scope call (AD-13).</summary>
    NoAdapterCaching,
}

/// <summary>The outcome of one check.</summary>
public enum AiConformanceOutcome
{
    /// <summary>The app behaved as the contract requires.</summary>
    Passed,

    /// <summary>The app did not.</summary>
    Failed,

    /// <summary>The check could not run here (no user given, nothing indexed, ...); the detail says why.</summary>
    NotApplicable,
}

/// <summary>One check of the conformance run.</summary>
/// <param name="Category">What it protects.</param>
/// <param name="Check">What it checks.</param>
/// <param name="Outcome">How it went.</param>
/// <param name="Detail">Why it failed or did not apply.</param>
public sealed record AiConformanceResult(AiConformanceCategory Category, string Check, AiConformanceOutcome Outcome, string? Detail = null)
{
    /// <inheritdoc />
    public override string ToString()
        => $"[{Outcome}] {Category}: {Check}" + (Detail is null ? string.Empty : $" ({Detail})");
}

/// <summary>The checks of one conformance run, each category independently (a connector passes or fails each one).</summary>
public sealed class AiConformanceReport
{
    /// <summary>Creates a report.</summary>
    public AiConformanceReport(IReadOnlyList<AiConformanceResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = results;
    }

    /// <summary>Every check, in the order they ran.</summary>
    public IReadOnlyList<AiConformanceResult> Results { get; }

    /// <summary>The checks that failed.</summary>
    public IReadOnlyList<AiConformanceResult> Failures => [.. Results.Where(r => r.Outcome == AiConformanceOutcome.Failed)];

    /// <summary>Whether every check of <paramref name="category"/> that ran passed (and at least one ran).</summary>
    public bool Passed(AiConformanceCategory category)
        => Results.Any(r => r.Category == category && r.Outcome == AiConformanceOutcome.Passed)
            && Results.All(r => r.Category != category || r.Outcome != AiConformanceOutcome.Failed);

    /// <summary>Throws <see cref="AiConformanceException"/> listing every failed check.</summary>
    public void EnsurePassed()
    {
        if (Failures.Count > 0)
            throw new AiConformanceException(this);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var result in Results)
            text.AppendLine(result.ToString());
        return text.ToString();
    }
}

/// <summary>Thrown by <see cref="AiConformanceReport.EnsurePassed"/>.</summary>
public sealed class AiConformanceException : Exception
{
    /// <summary>Creates the exception for <paramref name="report"/>.</summary>
    public AiConformanceException(AiConformanceReport report)
        : base(Describe(report))
    {
        Report = report;
    }

    /// <summary>The report.</summary>
    public AiConformanceReport Report { get; }

    private static string Describe(AiConformanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return $"The AI connector failed {report.Failures.Count} conformance check(s):{Environment.NewLine}"
            + string.Join(Environment.NewLine, report.Failures.Select(f => "  " + f));
    }
}

/// <summary>What a conformance run may use from the app under test.</summary>
public sealed class AiConformanceOptions
{
    /// <summary>
    /// An account the connector resolves (the value of its user claim: by default the account id), with access to some
    /// of the app's data. Without it, only the checks that need no user run; the rest are not applicable.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Changes what <see cref="User"/> may do, in the app (e.g. removes a role). The run then checks that the next scope
    /// call reflects the change (AD-13). Runs last. Without it, that check is not applicable.
    /// </summary>
    public Func<IServiceProvider, CancellationToken, Task>? ChangeUserAccess { get; set; }

    /// <summary>
    /// Makes the app report an access change through its own code path. By default every registered
    /// <c>IAccessChangeObserver</c> is told of a simulated grant change, as the platform's suite simulates one.
    /// </summary>
    public Func<IServiceProvider, CancellationToken, Task>? TriggerAccessChange { get; set; }

    /// <summary>How long to wait for revocation signals.</summary>
    public TimeSpan RevocationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The page size used to walk <c>/extract</c>.</summary>
    public int ExtractPageSize { get; set; } = 2;

    /// <summary>The most <c>/extract</c> pages walked.</summary>
    public int MaxExtractPages { get; set; } = 25;
}
