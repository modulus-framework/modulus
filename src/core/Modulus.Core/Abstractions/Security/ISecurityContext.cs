namespace Modulus.Core.Abstractions;

/// <summary>
/// The one view of "who is acting, where, and through what" that authorization, audit and data
/// isolation read. It composes the existing accessors (<see cref="ICurrentUser"/>,
/// <see cref="ICurrentTenant"/>, <see cref="ICurrentDataScope"/>, <see cref="ICorrelationContext"/>)
/// rather than replacing them. Company = tenant; a branch is an org unit of that tenant.
/// <para>
/// HTTP requests, background jobs and message consumers all see the same shape: a job or consumer
/// gets its company from the verified tenant restore, never from an unchecked message field.
/// </para>
/// </summary>
public interface ISecurityContext
{
    /// <summary>What kind of principal is acting.</summary>
    SecurityPrincipalKind Kind { get; }

    /// <summary>The acting user (an anonymous user when <see cref="Kind"/> is not <see cref="SecurityPrincipalKind.User"/>).</summary>
    ICurrentUser User { get; }

    /// <summary>The company (tenant) in scope; null in host scope or when none was resolved.</summary>
    Guid? CompanyId { get; }

    /// <summary>The group of companies <see cref="CompanyId"/> belongs to, when known.</summary>
    Guid? GroupId { get; }

    /// <summary>The branch (org unit) the caller selected, already checked against the caller's org scope.</summary>
    Guid? BranchId { get; }

    /// <summary>The verified network path. <see cref="NetworkContext.Unverified"/> until a network trust mechanism is configured.</summary>
    NetworkContext Network { get; }

    /// <summary>The AI agent acting on the user's behalf; null when a human acts directly.</summary>
    AgentContext? Agent { get; }

    /// <summary>The business correlation id of the current flow.</summary>
    string? CorrelationId { get; }
}

/// <summary>The kind of principal behind an <see cref="ISecurityContext"/>.</summary>
public enum SecurityPrincipalKind
{
    /// <summary>No authenticated caller (an anonymous HTTP request).</summary>
    Anonymous = 0,

    /// <summary>An authenticated user acting in a company (or with no company resolved).</summary>
    User = 1,

    /// <summary>An authenticated user in the explicit all-tenants host scope.</summary>
    Host = 2,

    /// <summary>Framework or application code outside a request (job, consumer, hosted service).</summary>
    System = 3,
}

/// <summary>How far the network path of a request is trusted.</summary>
public enum NetworkTrust
{
    /// <summary>No verified network claim (the default; header-derived addresses never count).</summary>
    Unverified = 0,

    /// <summary>Arrived through a registered gateway of the company.</summary>
    CompanyNetwork = 1,

    /// <summary>Arrived through the gateway of a specific branch.</summary>
    BranchNetwork = 2,

    /// <summary>Service-to-service traffic with a verified service identity.</summary>
    Internal = 3,
}

/// <summary>The verified network path of a request.</summary>
/// <param name="Trust">The verified trust level.</param>
/// <param name="GatewayId">The gateway the request came through, when verified.</param>
public sealed record NetworkContext(NetworkTrust Trust, string? GatewayId = null)
{
    /// <summary>No verified network claim.</summary>
    public static NetworkContext Unverified { get; } = new(NetworkTrust.Unverified);
}

/// <summary>An AI agent acting on a user's behalf. Its rights are the intersection of the user's and its own.</summary>
/// <param name="AgentId">The agent's identity.</param>
/// <param name="ToolName">The tool being invoked, when the call comes through a tool.</param>
public sealed record AgentContext(string AgentId, string? ToolName = null);
