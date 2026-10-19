namespace Modulus.Identity.Abstractions;

/// <summary>
/// Reads an account's stored lifecycle and records lifecycle transitions. The user manager uses it so that every lifecycle change
/// is written in the same save as the account. Registered by <c>AddModulusIdentity</c> over the Identity context.
/// </summary>
public interface IAccountTransitionStore
{
    /// <summary>The lifecycle as stored (not as changed in memory); null when the account is not stored yet.</summary>
    Task<AccountLifecycle?> GetStoredLifecycleAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Adds a transition to the pending save. It is written with the account change that caused it.</summary>
    void Record(ModulusAccountTransition transition);
}
