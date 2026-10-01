namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Modulus.Identity.Abstractions;

/// <summary>
/// Validates password-grant credentials against ASP.NET Core Identity via
/// <see cref="SignInManager{TUser}"/>. Rejects inactive users and honours
/// lock-out. Registered by <c>AddModulusIdentity</c> in place of
/// <see cref="NullPasswordGrantCredentialValidator"/>.
/// <para>
/// Every denial costs one password hash. Without that, an unknown user name (and
/// an inactive, unconfirmed or locked-out account) was refused before any hash
/// ran, so the response time told a caller which user names exist.
/// </para>
/// </summary>
internal sealed class IdentityPasswordGrantValidator<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager)
    : IPasswordGrantCredentialValidator
    where TUser : ModulusUser, new()
{
    // A hash of a random password, verified on denials that would otherwise skip
    // the hasher. Built lazily with the app's own hasher so it costs the same
    // (iteration count, algorithm) as checking a real account; a race on first
    // use only builds it twice.
    private static string? s_dummyHash;

    public async Task<PasswordGrantResult> ValidateAsync(
        string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return PasswordGrantResult.Denied();
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return DenyAfterHashing(password);
        }

        if (!user.IsActive)
        {
            return DenyAfterHashing(password, "account_disabled");
        }

        // Honor the SignIn requirements (RequireConfirmedEmail /
        // RequireConfirmedPhoneNumber / RequireConfirmedAccount).
        // CheckPasswordSignInAsync deliberately does NOT enforce these —
        // skipping this check would let unconfirmed accounts obtain tokens
        // when Identity:RequireConfirmedEmail=true.
        if (!await signInManager.CanSignInAsync(user))
        {
            return DenyAfterHashing(password);
        }

        var check = await signInManager.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);

        if (!check.Succeeded)
        {
            // A locked-out account is refused before its password is checked.
            return check.IsLockedOut
                ? DenyAfterHashing(password)
                : PasswordGrantResult.Denied();
        }

        var roles = await userManager.GetRolesAsync(user);
        var securityStamp = await userManager.GetSecurityStampAsync(user);

        return new PasswordGrantResult
        {
            Success = true,
            Subject = await userManager.GetUserIdAsync(user),
            // The user name, not FullName: the name claim becomes ICurrentUser.UserName and the audit columns, so it
            // must be unique and match what a refresh re-issues.
            UserName = await userManager.GetUserNameAsync(user),
            TenantId = user.TenantId,
            Email = await userManager.GetEmailAsync(user),
            Roles = roles.ToList(),
            SecurityStamp = securityStamp,
        };
    }

    private PasswordGrantResult DenyAfterHashing(string password, string error = "invalid_grant")
    {
        var hasher = userManager.PasswordHasher;
        var dummy = new TUser();
        s_dummyHash ??= hasher.HashPassword(dummy, Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(dummy, s_dummyHash, password);
        return PasswordGrantResult.Denied(error);
    }
}
