namespace Modulus.Core.Abstractions;

/// <summary>
/// What a parent company's administrator may do in one of its child companies (decision 6). Each operation needs its own grant:
/// a <see cref="ManageMembers"/> grant does not allow a grant operation.
/// </summary>
public enum CompanyOperation
{
    /// <summary>Add and remove the child's members.</summary>
    ManageMembers = 0,

    /// <summary>Change the child's permission grants (for example re-apply its baseline).</summary>
    ManageGrants = 1,
}

/// <summary>One explicit authority of a parent company's administrator over a child company. Revoked rows are kept, not deleted.</summary>
/// <param name="Id">The grant's id.</param>
/// <param name="ParentId">The parent company that holds the authority.</param>
/// <param name="ChildId">The child company it applies to.</param>
/// <param name="PrincipalId">The account that holds it.</param>
/// <param name="Operation">What the account may do in the child.</param>
/// <param name="GrantedBy">The host administrator who granted it.</param>
/// <param name="GrantedAt">When it was granted.</param>
/// <param name="RevokedAt">When it was revoked, or null while it is in force.</param>
/// <param name="RevokedBy">Who revoked it, or null while it is in force.</param>
public sealed record CompanyAuthorityGrant(
    Guid Id,
    Guid ParentId,
    Guid ChildId,
    Guid PrincipalId,
    CompanyOperation Operation,
    Guid? GrantedBy,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt = null,
    Guid? RevokedBy = null)
{
    /// <summary>Whether the grant is in force (not revoked).</summary>
    public bool IsActive => RevokedAt is null;
}

/// <summary>
/// Stores the explicit parent-to-child authorities (<c>CompanyAuthorityGrant</c>). Implemented by the authorization EF store.
/// Reads are direct queries and are never cached: a revoked authority stops applying on the caller's next request.
/// </summary>
public interface ICompanyAuthorityStore
{
    /// <summary>Whether <paramref name="principalId"/> holds a grant for exactly this parent, child and operation that is not revoked.</summary>
    Task<bool> HasActiveGrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, CancellationToken ct = default);

    /// <summary>Records a new grant. A grant that is already in force for the same key is returned unchanged.</summary>
    Task<CompanyAuthorityGrant> GrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, Guid? grantedBy, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Revokes a grant (the row is kept). Returns false when there is no grant with that id, or it is already revoked.</summary>
    Task<bool> RevokeAsync(Guid id, Guid? revokedBy, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Every grant between one parent and one child, revoked ones included, oldest first.</summary>
    Task<IReadOnlyList<CompanyAuthorityGrant>> ListAsync(Guid parentId, Guid childId, CancellationToken ct = default);

    /// <summary>Returns one grant by id, or null.</summary>
    Task<CompanyAuthorityGrant?> FindAsync(Guid id, CancellationToken ct = default);
}
