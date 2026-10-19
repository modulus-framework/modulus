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

    /// <summary>How the person authenticated (RFC 8176 values such as <c>pwd</c>, <c>otp</c>, <c>mfa</c>, <c>hwk</c>); access token only.</summary>
    internal const string MethodClaim = "amr";

    /// <summary>The session this token belongs to (<see cref="Modulus.Identity.Abstractions.ModulusSession"/>); a refresh keeps it.</summary>
    internal const string SessionClaim = "sid";

    /// <summary>When the person authenticated, in seconds since the Unix epoch; a refresh keeps the original value.</summary>
    internal const string AuthTimeClaim = "auth_time";

    /// <summary>
    /// When the Identity cookie was first issued, in seconds since the Unix epoch. Kept across sliding renewals (the cookie's
    /// <c>IssuedUtc</c> is renewed by each one), so the absolute session lifetime and <c>max_age</c> measure from the sign-in. A cookie
    /// claim only: <c>BuildPrincipal</c> never copies it into a token.
    /// </summary>
    internal const string SignedInAtClaim = "modulus_signed_in_at";

    /// <summary>The <c>amr</c> values of a password sign-in, with the second factor when one was verified.</summary>
    internal static string[] PasswordMethods(bool secondFactorVerified)
        => secondFactorVerified ? ["pwd", "otp", "mfa"] : ["pwd"];

    private static readonly MethodInfo s_revalidate = typeof(TokenPrincipalFactory)
        .GetMethod(nameof(RevalidateTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"{nameof(RevalidateTypedAsync)} not found.");

    internal static ClaimsPrincipal Create(
        string subject,
        string? userName,
        string? email,
        Guid? tenantId,
        IEnumerable<string> roles,
        string? securityStamp,
        IEnumerable<string>? methods = null,
        DateTimeOffset? authenticatedAt = null)
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

        // How and when the person signed in. Step-up authentication reads these from the access token.
        if (methods is not null)
        {
            foreach (var method in methods.Distinct(StringComparer.Ordinal))
                identity.AddClaim(new Claim(MethodClaim, method));
            identity.AddClaim(new Claim(
                AuthTimeClaim,
                (authenticatedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64));
        }

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
        if (user is null || !user.MayStartNewAccess())
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
        // How and when the person signed in is carried over unchanged: a refresh must not turn an old sign-in into a fresh one.
        var methods = grant.FindAll(MethodClaim).Select(c => c.Value).ToArray();
        DateTimeOffset? authenticatedAt = long.TryParse(grant.FindFirstValue(AuthTimeClaim), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var rebuilt = Create(
            await userManager.GetUserIdAsync(user),
            await userManager.GetUserNameAsync(user),
            await userManager.GetEmailAsync(user),
            user.TenantId,
            await userManager.GetRolesAsync(user),
            currentStamp,
            methods.Length > 0 ? methods : null,
            authenticatedAt);
        if (grant.FindFirstValue(SessionClaim) is { Length: > 0 } sessionId)
            ((ClaimsIdentity)rebuilt.Identity!).AddClaim(new Claim(SessionClaim, sessionId));
        return rebuilt;
    }
}
