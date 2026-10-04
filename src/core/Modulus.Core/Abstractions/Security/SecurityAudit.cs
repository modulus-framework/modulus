namespace Modulus.Core.Abstractions;

/// <summary>
/// The security audit trail: tenant rejections, break-glass entries, membership and permission changes, sign-ins,
/// cross-tenant SQL opt-ins and the startup loosening report. It is kept apart from the business audit log
/// (who changed which order) and, with <c>AddModulusSecurityAudit</c> (Modulus.Platform), written as one
/// hash chain per tenant, so a deleted or edited entry is detectable.
/// </summary>
/// <remarks>
/// <see cref="Record"/> never blocks and never throws: sources include synchronous code paths (EF Core command
/// interceptors) and rejection paths that must still answer <c>403</c>. The default is
/// <see cref="NullSecurityAuditLog"/>.
/// </remarks>
public interface ISecurityAuditLog
{
    /// <summary>Queues one event for the chain of <see cref="SecurityAuditEvent.TenantId"/>.</summary>
    void Record(SecurityAuditEvent auditEvent);
}

/// <summary>Discards every event (the default until <c>AddModulusSecurityAudit</c> is called).</summary>
public sealed class NullSecurityAuditLog : ISecurityAuditLog
{
    /// <summary>The shared instance.</summary>
    public static NullSecurityAuditLog Instance { get; } = new();

    private NullSecurityAuditLog()
    {
    }

    /// <inheritdoc />
    public void Record(SecurityAuditEvent auditEvent)
    {
    }
}

/// <summary>One security-relevant fact, before it is sequenced and hashed into its tenant's chain.</summary>
public sealed record SecurityAuditEvent
{
    /// <summary>The area, one of <see cref="SecurityAuditCategories"/> (or an application's own).</summary>
    public required string Category { get; init; }

    /// <summary>What happened, e.g. <c>tenant.rejected</c>, <c>signin.failed</c>.</summary>
    public required string Action { get; init; }

    /// <summary>One of <see cref="SecurityAuditOutcomes"/>.</summary>
    public string Outcome { get; init; } = SecurityAuditOutcomes.Success;

    /// <summary>The company whose chain receives the event; null = the host chain.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Who acted: a user id, a client id, or null when unknown (anonymous, system).</summary>
    public string? Actor { get; init; }

    /// <summary>What was acted on, e.g. <c>tenant:3fa8…</c>, <c>role:Admin</c>, a table list.</summary>
    public string? Target { get; init; }

    /// <summary>The business correlation id of the flow that produced the event.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>When it happened; null = when the log receives it.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>Extra key/value context. Never put secrets or personal data here: the chain is append-only.</summary>
    public IReadOnlyDictionary<string, string?> Details { get; init; } = new Dictionary<string, string?>();
}

/// <summary>Built-in <see cref="SecurityAuditEvent.Category"/> values.</summary>
public static class SecurityAuditCategories
{
    /// <summary>Company selection, membership and break-glass.</summary>
    public const string Tenancy = "tenancy";

    /// <summary>Permission grants, role and org changes, audited access decisions.</summary>
    public const string Authorization = "authorization";

    /// <summary>Sign-in, token refresh and token issue.</summary>
    public const string Identity = "identity";

    /// <summary>Data-isolation events (cross-tenant SQL opt-ins and rejections).</summary>
    public const string Data = "data";

    /// <summary>Security configuration at startup (the loosening report).</summary>
    public const string Configuration = "configuration";

    /// <summary>Calls from an external AI platform (the AI connector): authentication, capabilities, lookups, checks.</summary>
    public const string Ai = "ai";
}

/// <summary>Built-in <see cref="SecurityAuditEvent.Outcome"/> values.</summary>
public static class SecurityAuditOutcomes
{
    /// <summary>The action happened.</summary>
    public const string Success = "success";

    /// <summary>A control refused the action.</summary>
    public const string Denied = "denied";

    /// <summary>A control let the action through only by an explicit override (break-glass, an opt-in scope).</summary>
    public const string Overridden = "overridden";
}
