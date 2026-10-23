using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;

namespace Modulus.Security;

/// <summary>
/// The <see cref="ISecurityContext"/> registered by <c>AddModulusSecurityContext()</c>. Scoped: a
/// request (or a job / consumer scope) sees one consistent view. The company comes from the ambient
/// tenant (verified by <see cref="TenantMiddleware"/> or the tenant restore of jobs and consumers),
/// the branch from <see cref="BranchContextMiddleware"/>.
/// </summary>
public sealed class SecurityContext(
    ICurrentUser user,
    ICurrentTenant tenant,
    CurrentBranch branch,
    IHttpContextAccessor? httpContextAccessor = null,
    ICorrelationContext? correlation = null) : ISecurityContext
{
    public SecurityPrincipalKind Kind
    {
        get
        {
            if (user.IsAuthenticated)
                return tenant.IsHost ? SecurityPrincipalKind.Host : SecurityPrincipalKind.User;

            return httpContextAccessor?.HttpContext is null
                ? SecurityPrincipalKind.System
                : SecurityPrincipalKind.Anonymous;
        }
    }

    public ICurrentUser User => user;

    public Guid? CompanyId => tenant.TenantId;

    public Guid? GroupId => (tenant as CurrentTenant)?.Tenant?.GroupId;

    public Guid? BranchId => branch.BranchId;

    public NetworkContext Network => NetworkContext.Unverified;

    // An agent acting for the person (A9-1): the request's act claim names the agent's role. Read from the request principal only;
    // a job or consumer has no request, so it has no agent.
    public AgentContext? Agent
    {
        get
        {
            var act = httpContextAccessor?.HttpContext?.User.FindFirst("act")?.Value;
            return string.IsNullOrWhiteSpace(act) ? null : new AgentContext(act);
        }
    }

    public string? CorrelationId => correlation?.CorrelationId;
}

/// <summary>
/// Holds the branch (org unit) selected for the current scope. Set only by
/// <see cref="BranchContextMiddleware"/> after checking the caller's org scope.
/// </summary>
public sealed class CurrentBranch
{
    /// <summary>The selected branch, or null when none was selected.</summary>
    public Guid? BranchId { get; private set; }

    internal void Set(Guid branchId) => BranchId = branchId;
}
