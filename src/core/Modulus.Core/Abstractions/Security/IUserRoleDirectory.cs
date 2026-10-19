namespace Modulus.Core.Abstractions;

/// <summary>
/// Answers "which roles does this user hold right now" from the system of record (the identity store), so
/// authorization administration never has to trust role names a caller typed into a request.
/// </summary>
public interface IUserRoleDirectory
{
    /// <summary>
    /// The roles of <paramref name="userId"/>, or <see langword="null"/> when the user is unknown or not visible
    /// to the current tenant (callers treat that as a refusal, never as "no roles").
    /// </summary>
    ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="userId"/> may start access now: the account exists and is enabled. A delegation made by a user who may not is
    /// ended (A9-2), so a directory that does not answer this refuses by default rather than keeping delegations alive.
    /// </summary>
    ValueTask<bool> MayStartAccessAsync(Guid userId, CancellationToken ct = default) => ValueTask.FromResult(false);
}
