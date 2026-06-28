namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Modulus.Identity.Abstractions;

/// <summary>
/// Validates password-grant credentials against ASP.NET Core Identity via
/// <see cref="SignInManager{TUser}"/>. Rejects inactive users and honours
/// lock-out. Registered by <c>AddModulusIdentity</c> in place of
/// <see cref="NullPasswordGrantCredentialValidator"/>.
/// </summary>
internal sealed class IdentityPasswordGrantValidator<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager)
    : IPasswordGrantCredentialValidator
    where TUser : ModulusUser, new()
{
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
            return PasswordGrantResult.Denied();
        }

        if (!user.IsActive)
        {
            return PasswordGrantResult.Denied("account_disabled");
        }

        var check = await signInManager.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);

        if (!check.Succeeded)
        {
            return PasswordGrantResult.Denied();
        }

        var roles = await userManager.GetRolesAsync(user);

        return new PasswordGrantResult
        {
            Success = true,
            Subject = await userManager.GetUserIdAsync(user),
            UserName = user.FullName,
            Email = await userManager.GetEmailAsync(user),
            Roles = roles.ToList(),
        };
    }
}
