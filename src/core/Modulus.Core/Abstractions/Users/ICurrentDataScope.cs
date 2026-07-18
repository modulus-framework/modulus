namespace Modulus.Core.Abstractions;

/// <summary>
/// Scoped accessor for the organizational data scope of the principal on the
/// current request — the set of org-unit ids their rows may be drawn from, plus an
/// unrestricted-context switch. It is the seam the <c>ModuleDbContext</c> org-scope
/// query filter reads, exactly as it reads <see cref="ICurrentTenant"/> for tenant
/// isolation.
/// <para>
/// Implemented by the Authorization module (bridges <see cref="ICurrentUser"/> +
/// the org-scope resolver); the framework falls back to
/// <see cref="Modulus.Core.Null.NullCurrentDataScope"/> — which is
/// <see cref="IsUnrestricted"/> — when organizational scoping is not configured, so
/// that merely marking an entity <see cref="Entities.IHasOrgUnit"/> without wiring a
/// resolver is a no-op filter rather than a lock-out (mirrors
/// <see cref="Modulus.Core.Null.NullCurrentTenant"/>).
/// </para>
/// <para>
/// <b>Fail-closed once configured:</b> when scoping <i>is</i> wired but the principal
/// resolves to no units (unauthenticated, no placement, a misconfigured resolver),
/// <see cref="IsUnrestricted"/> is <see langword="false"/> and <see cref="OrgUnitIds"/>
/// is empty, so the org-scope filter matches <b>nothing</b> — never every unit's rows.
/// Seeing across the whole organization must be a deliberate act (an unrestricted
/// principal), never an accident.
/// </para>
/// </summary>
public interface ICurrentDataScope
{
    /// <summary>
    /// True when the current principal may see rows in <b>any</b> org unit — either
    /// because organizational scoping is not configured
    /// (<see cref="Modulus.Core.Null.NullCurrentDataScope"/>), or because the
    /// principal holds the scope-bypass grant. This is the seam that keeps org
    /// filtering fail-closed: when scoping is configured but nothing resolved,
    /// this is <see langword="false"/> and <see cref="OrgUnitIds"/> is empty.
    /// </summary>
    bool IsUnrestricted { get; }

    /// <summary>
    /// The org-unit ids the current principal is scoped to — their placements
    /// already expanded by traversal mode over the hierarchy closure and unioned.
    /// Empty when unauthenticated or unplaced (fail-closed). Ignored when
    /// <see cref="IsUnrestricted"/> is <see langword="true"/>.
    /// </summary>
    IReadOnlyCollection<Guid> OrgUnitIds { get; }
}
