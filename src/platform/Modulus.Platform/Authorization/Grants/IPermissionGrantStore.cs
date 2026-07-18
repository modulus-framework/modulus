namespace Modulus.Authorization.Grants;

/// <summary>
/// Stores authorization grants as data and returns those applicable to a
/// principal. The store is the editable, revocable source of truth for
/// <em>which principal holds which capability</em> — the piece Modulus was
/// missing (blueprint §22): previously effective permissions arrived only as
/// issuer-asserted JWT claims.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent reads while grants are being
/// mutated. Lookups are synchronous because they sit behind the synchronous
/// <see cref="Modulus.Core.Abstractions.ICurrentUser.HasPermission"/>; a
/// persistent (e.g. EF-backed) store is expected to load a principal's grants
/// once per scope. The default <see cref="InMemoryPermissionGrantStore"/> holds
/// grants in memory.
/// </remarks>
public interface IPermissionGrantStore
{
    /// <summary>
    /// Returns every grant (allow and deny) attached to the principal's user id
    /// or any of its roles. Order is not significant — the resolver applies
    /// deny-override regardless of ordering. An empty result means "no grants",
    /// which resolves fail-closed to no permissions.
    /// </summary>
    IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal);
}
