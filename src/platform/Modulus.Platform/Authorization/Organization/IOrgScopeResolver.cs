namespace Modulus.Authorization.Organization;

/// <summary>
/// Resolves a principal's effective <see cref="OrgScope"/> — the union of every
/// placement expanded by its traversal mode over the hierarchy closure. This is
/// the organizational layer of the authorization pipeline (blueprint §7): given a
/// user <em>can</em> perform an action, it answers <em>where in the organization</em>
/// they may. The resulting scope is the input the data-scope layer composes into a
/// query predicate. Resolution is fail-closed: an anonymous principal or a user
/// with no placements resolves to <see cref="OrgScope.None"/>.
/// </summary>
public interface IOrgScopeResolver
{
    /// <summary>
    /// The org units the given user is entitled to act within. A <c>null</c> user
    /// (anonymous) or a user with no placements resolves to <see cref="OrgScope.None"/>.
    /// </summary>
    OrgScope Resolve(Guid? userId);
}
