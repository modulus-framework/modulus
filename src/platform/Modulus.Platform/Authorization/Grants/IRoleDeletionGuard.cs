namespace Modulus.Authorization.Grants;

/// <summary>
/// Decides whether a role may be deleted (premodule 1, decision D-P1). A role that still holds permission grants is cleared first, so no
/// grant is left pointing at a role that no longer exists. The authorization management assembly implements it over the grant store. With
/// no implementation registered, no role can be deleted.
/// </summary>
public interface IRoleDeletionGuard
{
    /// <summary>Null when the role may be deleted; otherwise the reason it may not.</summary>
    Task<string?> BlockingReasonAsync(string role, CancellationToken cancellationToken);
}
