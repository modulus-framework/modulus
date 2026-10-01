using System.Reflection;
using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;

/// <summary>
/// Builds the principals the token endpoint signs, so every grant (password, refresh, authorization code) issues the
/// same claims: <c>sub</c>, <c>name</c> (always the user name, never the display name: <c>ICurrentUser.UserName</c>
/// feeds the audit columns, so it must be stable and unique), <c>email</c>, <c>role</c>s, the security stamp and, for
/// a user that belongs to a tenant, <see cref="TenantClaim"/> (what <c>UseJwtClaimResolver()</c> reads and cross-checks
/// against a header- or subdomain-resolved tenant).
/// </summary>
internal static class TokenPrincipalFactory
{
    /// <summary>The tenant claim, matching <c>UseJwtClaimResolver()</c>'s default claim type.</summary>
    internal const string TenantClaim = "tid";

    /// <summary>The claim carrying the ASP.NET Core Identity security stamp (access token only).</summary>
    internal const string SecurityStampClaim = "security_stamp";

    private static readonly MethodInfo s_revalidate = typeof(TokenPrincipalFactory)
        .GetMethod(nameof(RevalidateTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"{nameof(RevalidateTypedAsync)} not found.");

    internal static ClaimsPrincipal Create(
        string subject,
        string? userName,
        string? email,
        Guid? tenantId,
        IEnumerable<string> roles,
        string? securityStamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(roles);

        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, subject));
        if (!string.IsNullOrWhiteSpace(userName))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, userName));
        if (!string.IsNullOrWhiteSpace(email))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, email));
        if (tenantId is { } tid && tid != Guid.Empty)
            identity.AddClaim(new Claim(TenantClaim, tid.ToString()));
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));

        // Lets the refresh handler notice a password change or another security invalidation without a DB round-trip on
        // every access-token use; access token only (see ModulusTokenController.ApplyDestinations).
        if (!string.IsNullOrWhiteSpace(securityStamp))
            identity.AddClaim(new Claim(SecurityStampClaim, securityStamp));

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Re-verifies the subject of a refresh token or redeemed authorization code against the current store and rebuilds
    /// its claims from it. Returns <see langword="null"/> when the grant must be refused: the user no longer exists, is
    /// inactive or locked out, or its security stamp changed since the token was issued.
    /// </summary>
    /// <param name="userManager">
    /// A <c>UserManager&lt;TUser&gt;</c> for the registered user type. Typed as <see cref="object"/> because
    /// <c>UserManager&lt;AppUser&gt;</c> is not a <c>UserManager&lt;ModulusUser&gt;</c> (classes are invariant), so a
    /// cast would be null for every derived user type and silently skip all of these checks.
    /// </param>
    /// <param name="grant">The principal carried by the refresh token or authorization code.</param>
    internal static Task<ClaimsPrincipal?> RevalidateAsync(object userManager, ClaimsPrincipal grant)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(grant);

        var managerType = userManager.GetType();
        while (managerType is not null
               && !(managerType.IsGenericType && managerType.GetGenericTypeDefinition() == typeof(UserManager<>)))
        {
            managerType = managerType.BaseType;
        }

        var userType = managerType?.GetGenericArguments()[0];
        if (userType is null || !typeof(ModulusUser).IsAssignableFrom(userType))
            throw new ArgumentException(
                $"Expected a UserManager<TUser> with TUser : {nameof(ModulusUser)}, got {userManager.GetType()}.",
                nameof(userManager));

        return (Task<ClaimsPrincipal?>)s_revalidate.MakeGenericMethod(userType).Invoke(null, [userManager, grant])!;
    }

    private static async Task<ClaimsPrincipal?> RevalidateTypedAsync<TUser>(
        UserManager<TUser> userManager, ClaimsPrincipal grant)
        where TUser : ModulusUser
    {
        var subject = grant.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject) ? null : await userManager.FindByIdAsync(subject);

        // A refresh token can outlive a disabled or deleted account.
        if (user is not { IsActive: true })
            return null;

        // Mirrors the sign-in flow's lock-out policy.
        if (await userManager.IsLockedOutAsync(user))
            return null;

        // A different stamp means the password changed or the user was otherwise security-invalidated since the token
        // was issued: the user must sign in again.
        var currentStamp = await userManager.GetSecurityStampAsync(user);
        var storedStamp = grant.FindFirstValue(SecurityStampClaim);
        if (!string.IsNullOrWhiteSpace(storedStamp)
            && !string.Equals(storedStamp, currentStamp, StringComparison.Ordinal))
        {
            return null;
        }

        // Rebuild from the CURRENT store state so a role change, demotion, tenant move or profile edit takes effect at
        // the next refresh instead of the next login.
        return Create(
            await userManager.GetUserIdAsync(user),
            await userManager.GetUserNameAsync(user),
            await userManager.GetEmailAsync(user),
            user.TenantId,
            await userManager.GetRolesAsync(user),
            currentStamp);
    }
}
