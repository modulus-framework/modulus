using Modulus.Core.Abstractions;

namespace Modulus.Core.Null;

/// <summary>
/// Default <see cref="ISecurityContext"/> when <c>AddModulusSecurityContext()</c> was not called:
/// built from the registered accessors with no branch, network or agent information.
/// </summary>
public sealed class NullSecurityContext(
    ICurrentUser user,
    ICurrentTenant tenant,
    ICorrelationContext? correlation = null) : ISecurityContext
{
    public SecurityPrincipalKind Kind => !user.IsAuthenticated
        ? SecurityPrincipalKind.Anonymous
        : tenant.IsHost ? SecurityPrincipalKind.Host : SecurityPrincipalKind.User;

    public ICurrentUser User => user;

    public Guid? CompanyId => tenant.TenantId;

    public Guid? GroupId => null;

    public Guid? BranchId => null;

    public NetworkContext Network => NetworkContext.Unverified;

    public AgentContext? Agent => null;

    public string? CorrelationId => correlation?.CorrelationId;
}
