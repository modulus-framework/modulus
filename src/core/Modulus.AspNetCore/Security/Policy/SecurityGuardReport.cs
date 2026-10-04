namespace Modulus.AspNetCore.Security.Policy;

using Modulus.Core.Abstractions.Security;

/// <summary>How an endpoint's callers are checked.</summary>
public enum EndpointAccess
{
    /// <summary>Explicit authorization data (a policy, permission, role or <c>[Authorize]</c>).</summary>
    Policed,

    /// <summary>No authorization data of its own; the fallback policy (a signed-in user) applies.</summary>
    Fallback,

    /// <summary>Open to anonymous callers.</summary>
    Loosened,

    /// <summary>No authorization data and no fallback policy: anyone may call it, unintentionally.</summary>
    Unpoliced,
}

/// <summary>One endpoint as the startup security guard resolved it.</summary>
/// <param name="Route">The route pattern (<c>/products/{id}</c>).</param>
/// <param name="Methods">The HTTP methods; empty = every method.</param>
/// <param name="DisplayName">The endpoint's display name.</param>
/// <param name="Access">How callers are checked.</param>
/// <param name="Policies">The authorization policies / roles (<c>(authenticated)</c> for a bare <c>[Authorize]</c>).</param>
/// <param name="LooseningReason">Why the endpoint is anonymous (its own reason, else the allow-list's).</param>
/// <param name="Ticket">The approving ticket, if any.</param>
/// <param name="AllowListed">Whether the loosening allow-list covers the endpoint.</param>
/// <param name="Classification">The declared data classification.</param>
/// <param name="Network">The declared network requirement.</param>
public sealed record EndpointSecurityEntry(
    string Route,
    IReadOnlyList<string> Methods,
    string? DisplayName,
    EndpointAccess Access,
    IReadOnlyList<string> Policies,
    string? LooseningReason,
    string? Ticket,
    bool AllowListed,
    DataClassification Classification,
    NetworkRequirement Network);

/// <summary>How serious a guard finding is.</summary>
public enum SecurityGuardSeverity
{
    /// <summary>Fails startup in every environment.</summary>
    Error,

    /// <summary>
    /// A reasoned loosening missing from the allow-list: fails startup outside Development, logs a
    /// warning in Development (unless <see cref="SecurityGuardOptions.FailOnUnlistedInDevelopment"/>).
    /// </summary>
    Unlisted,
}

/// <summary>A problem the guard found on an endpoint.</summary>
/// <param name="Severity">Whether it fails startup everywhere or only outside Development.</param>
/// <param name="Route">The methods and route (<c>GET /raw</c>).</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Code"><c>unpoliced</c>, <c>anonymous-without-reason</c>, <c>loosening-not-allow-listed</c>, <c>anonymous-classified-data</c> or <c>network-not-enforced</c>.</param>
public sealed record SecurityGuardFinding(SecurityGuardSeverity Severity, string Code, string Route, string Message);

/// <summary>Every endpoint the guard saw and what it found.</summary>
public sealed record SecurityGuardReport(
    IReadOnlyList<EndpointSecurityEntry> Endpoints,
    IReadOnlyList<SecurityGuardFinding> Findings)
{
    /// <summary>An empty report (before the guard ran).</summary>
    public static SecurityGuardReport Empty { get; } = new([], []);

    /// <summary>The anonymous endpoints, for the loosening report.</summary>
    public IEnumerable<EndpointSecurityEntry> Loosened => Endpoints.Where(e => e.Access == EndpointAccess.Loosened);

    /// <summary>
    /// GraphQL fields, realtime topics and other items carried by one endpoint, from every registered
    /// <see cref="ISecuritySurfaceContributor"/>. Reported, not judged: each item sits behind an endpoint the guard checked.
    /// </summary>
    public IReadOnlyList<SecuritySurfaceEntry> Surfaces { get; init; } = [];
}

/// <summary>Holds the report of the last guard run (for diagnostics, the security audit and tests).</summary>
public sealed class SecurityGuardState
{
    /// <summary>The last report; <see cref="SecurityGuardReport.Empty"/> before the guard ran.</summary>
    public SecurityGuardReport Report { get; internal set; } = SecurityGuardReport.Empty;
}

/// <summary>The guard refused to start the application.</summary>
public sealed class SecurityGuardException(IReadOnlyList<SecurityGuardFinding> findings)
    : InvalidOperationException(Describe(findings))
{
    /// <summary>The findings that failed startup.</summary>
    public IReadOnlyList<SecurityGuardFinding> Findings { get; } = findings;

    private static string Describe(IReadOnlyList<SecurityGuardFinding> findings)
        => "The security guard refused to start the application:" + Environment.NewLine
           + string.Join(Environment.NewLine, findings.Select(f => $"  [{f.Code}] {f.Route}: {f.Message}"));
}
