namespace Modulus.Authorization.Governance;

using Modulus.Authorization.Grants;

/// <summary>
/// A point-in-time snapshot of everything a principal can do — their direct capability
/// grants, the authority currently delegated to them (with on-behalf-of provenance), the
/// union the enforcement layer actually sees, and any segregation-of-duties violations in
/// that union. This is the artefact auditors and breach investigators ask for ("what could
/// this user access?") and the input a recertification campaign reviews (blueprint §5.14,
/// §16).
/// </summary>
/// <param name="UserId">The principal, or <see langword="null"/> if anonymous.</param>
/// <param name="DirectPermissions">Capability-layer permissions held directly (grants/roles).</param>
/// <param name="DelegatedPermissions">Permissions in force via active delegations, with provenance.</param>
/// <param name="AllPermissions">The effective union the PDP enforces.</param>
/// <param name="SodViolations">Toxic combinations present in <paramref name="AllPermissions"/>.</param>
public sealed record EffectiveAccessReport(
    Guid? UserId,
    IReadOnlySet<string> DirectPermissions,
    IReadOnlyCollection<DelegatedPermission> DelegatedPermissions,
    IReadOnlySet<string> AllPermissions,
    IReadOnlyCollection<SodViolation> SodViolations);

/// <summary>
/// Produces <see cref="EffectiveAccessReport"/>s by composing the capability resolver, the
/// delegation resolver, and the SoD policy — the governance/reporting entry point of the
/// authorization framework (blueprint §5.14, §16). It reads the <b>direct</b> capability
/// resolver (not the delegation-aware decorator) so direct and delegated authority are
/// reported distinctly rather than conflated.
/// </summary>
public interface IEffectiveAccessService
{
    /// <summary>Builds the effective-access snapshot for <paramref name="principal"/>.</summary>
    EffectiveAccessReport Report(PrincipalGrantQuery principal);
}

/// <inheritdoc cref="IEffectiveAccessService"/>
public sealed class EffectiveAccessService(
    PermissionResolver directAuthority,
    IDelegationResolver delegationResolver,
    ISodPolicy sodPolicy) : IEffectiveAccessService
{
    public EffectiveAccessReport Report(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var direct = directAuthority.Resolve(principal);
        var delegated = principal.UserId is { } userId
            ? delegationResolver.DelegatedPermissions(userId)
            : [];

        var all = new HashSet<string>(direct, StringComparer.OrdinalIgnoreCase);
        foreach (var permission in delegated)
            all.Add(permission.Permission);

        var violations = sodPolicy.Evaluate(all);

        return new EffectiveAccessReport(principal.UserId, direct, delegated, all, violations);
    }
}
