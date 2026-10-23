namespace Modulus.AuditLogging.Security.Retention;

/// <summary>How long security audit entries are kept in the live store before retention archives and purges them (A11-1).</summary>
public sealed class SecurityAuditRetentionOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Security:Retention";

    /// <summary>Whether retention runs. On by default; with no archive registered it still purges nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The age after which an entry may be purged. Default seven years, the owner memo's proposed product baseline (not a legal determination); an
    /// operator sets the legal period for the deployment.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(2555);

    /// <summary>How often the scheduled host runs retention over every chain. Default one day.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long one replica holds the cluster-wide retention lock. It must outlast a run; the lock is released at once when the run ends,
    /// and expires on its own if the replica dies mid-run. Default 30 minutes.
    /// </summary>
    public TimeSpan LockLease { get; set; } = TimeSpan.FromMinutes(30);
}
