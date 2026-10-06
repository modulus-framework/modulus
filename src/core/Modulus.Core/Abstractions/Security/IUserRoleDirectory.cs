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
}
