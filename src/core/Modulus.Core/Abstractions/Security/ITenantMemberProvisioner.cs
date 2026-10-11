namespace Modulus.Core.Abstractions;

/// <summary>
/// Adds and removes company members on behalf of a provisioning channel (SCIM, bulk import). The company's seat limit is
/// enforced here: <see cref="AddMemberAsync"/> throws <see cref="Exceptions.ConflictException"/> when the company has no
/// seat left. Implemented by the tenant store package; provisioning code treats a missing registration as "no limits".
/// </summary>
public interface ITenantMemberProvisioner
{
    /// <summary>Makes the user an active member of the company; false when the company does not exist.</summary>
    Task<bool> AddMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Ends the user's membership (freeing the seat); false when there was none.</summary>
    Task<bool> RemoveMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}
