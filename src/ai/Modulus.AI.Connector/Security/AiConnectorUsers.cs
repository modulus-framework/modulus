namespace Modulus.AI.Connector;

using Microsoft.AspNetCore.Identity;

/// <summary>
/// Finds the Modulus account the platform's envelope names. Return null for an unknown, disabled or locked-out
/// account: the call is then refused. The connector never creates accounts and never widens what an account may do;
/// permissions come from the app's own grant store for the returned id and roles.
/// </summary>
public interface IAiConnectorUserResolver
{
    /// <summary>The active account matching <paramref name="lookup"/>, or null.</summary>
    Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default);
}

/// <summary>What the envelope says about the user.</summary>
/// <param name="Value">The value of the configured user claim (<see cref="AiUserMatchOptions.Claim"/>).</param>
/// <param name="MatchBy">What <paramref name="Value"/> is matched against.</param>
/// <param name="EnvelopeClaims">Every claim of the verified envelope.</param>
public sealed record AiUserLookup(string Value, AiUserMatch MatchBy, IReadOnlyDictionary<string, string> EnvelopeClaims);

/// <summary>The account a call runs as.</summary>
/// <param name="UserId">The account id (the grant store's user id).</param>
/// <param name="UserName">The user name, when known.</param>
/// <param name="Email">The e-mail address, when known.</param>
/// <param name="Roles">The account's roles (the grant store's role holders).</param>
public sealed record AiConnectorUser(Guid UserId, string? UserName, string? Email, IReadOnlyList<string> Roles);

/// <summary>Refuses every user: the default until the app chooses a resolver, so nothing runs by accident.</summary>
internal sealed class DenyAllAiConnectorUserResolver : IAiConnectorUserResolver
{
    public Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default)
        => Task.FromResult<AiConnectorUser?>(null);
}

/// <summary>
/// Resolves accounts through ASP.NET Core Identity's <see cref="UserManager{TUser}"/>: by id, e-mail or user name;
/// a locked-out account, or one <c>isActive</c> rejects, is refused.
/// </summary>
internal sealed class IdentityAiConnectorUserResolver<TUser>(UserManager<TUser> users, Func<TUser, bool>? isActive)
    : IAiConnectorUserResolver
    where TUser : IdentityUser<Guid>
{
    public async Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var user = lookup.MatchBy switch
        {
            AiUserMatch.Id => Guid.TryParse(lookup.Value, out var id) ? await users.FindByIdAsync(id.ToString()) : null,
            AiUserMatch.Email => await users.FindByEmailAsync(lookup.Value),
            AiUserMatch.UserName => await users.FindByNameAsync(lookup.Value),
            _ => null,
        };

        if (user is null || (isActive is not null && !isActive(user)) || await users.IsLockedOutAsync(user))
            return null;

        var roles = await users.GetRolesAsync(user);
        return new AiConnectorUser(user.Id, user.UserName, user.Email, [.. roles]);
    }
}
