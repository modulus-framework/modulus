namespace Modulus.Authorization.Organization;

using Modulus.Core.Abstractions;

/// <summary>
/// Bridges <see cref="ICurrentDataScope"/> to the organizational model: resolves the
/// current principal's <see cref="OrgScope"/> from its <em>identity</em>
/// (<see cref="ICurrentUser.UserId"/> → placements → traversal closure) rather than
/// trusting scope claims on the token (blueprint §22). Registered scoped, so the
/// scope is resolved at most once per request and stays request-consistent even if
/// placements are mutated mid-request.
/// <para>
/// Fail-closed: an unauthenticated or unplaced principal resolves to
/// <see cref="OrgScope.None"/> — no units, not unrestricted — so the org-scope query
/// filter matches nothing. A principal holding <see cref="BypassPermission"/> is
/// unrestricted and sees every unit (the deliberate cross-organization act).
/// </para>
/// </summary>
public sealed class CurrentDataScope(ICurrentUser currentUser, IOrgScopeResolver resolver)
    : ICurrentDataScope
{
    /// <summary>
    /// The grant that lifts organizational row-scoping for a principal (e.g. a
    /// tenant-wide administrator or an all-org reporting role). Deny-by-default:
    /// no one is unrestricted unless this permission is explicitly granted.
    /// </summary>
    public const string BypassPermission = "data:scope:bypass";

    private Guid[]? _units;

    public bool IsUnrestricted => currentUser.HasPermission(BypassPermission);

    public IReadOnlyCollection<Guid> OrgUnitIds
        => _units ??= [.. resolver.Resolve(currentUser.UserId).Units];
}
