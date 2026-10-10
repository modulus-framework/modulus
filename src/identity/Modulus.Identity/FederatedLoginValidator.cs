namespace Modulus.Identity;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

/// <summary>
/// Maps a validated external token to a local account, the federated counterpart of
/// <see cref="IdentityPasswordGrantValidator{TUser}"/>. Registered by <c>AddModulusFederatedLogin</c>.
/// <para>
/// Accounts are linked by the provider's subject, stored as an <c>IdentityUserLogins</c> row. A new user gets an account
/// with no password, so it can only sign in through the provider. An existing account with the same email is linked only
/// when the provider verified that email and <c>LinkByVerifiedEmail</c> is on. Mapped roles are synced at every exchange:
/// roles that appear in <c>RoleMap</c> or <c>DefaultRoles</c> follow the provider, and roles granted locally are left alone.
/// </para>
/// <para>
/// Every denial is recorded in the security audit with its reason and no identifying values. The caller gets the same
/// generic <c>invalid_grant</c> whatever the reason, so the response does not reveal which accounts exist.
/// </para>
/// </summary>
internal sealed class FederatedLoginValidator<TUser, TRole>(
    IFederatedTokenReader reader,
    IOptions<FederatedLoginOptions> options,
    UserManager<TUser> userManager,
    RoleManager<TRole> roleManager,
    ISecurityAuditLog? audit = null)
    : IFederatedLoginValidator
    where TUser : ModulusUser, new()
    where TRole : ModulusRole
{
    public async Task<PasswordGrantResult> ValidateAsync(string subjectToken, CancellationToken ct = default)
    {
        var settings = options.Value;

        var principal = await reader.ReadAsync(subjectToken, ct);
        if (principal is null)
            return Denied(null, "invalid-token");

        var external = FederatedClaims.Read(principal, settings);
        if (external is null)
            return Denied(null, "no-subject");

        if (!FederatedClaims.EmailDomainAllowed(external.Email, settings.AllowedEmailDomains))
            return Denied(null, "email-domain-not-allowed");

        var (user, reason) = await ResolveAccountAsync(external, settings);
        if (user is null)
            return Denied(null, reason ?? "no-account");

        if (!user.IsActive)
            return Denied(user, "account-disabled");

        if (await userManager.IsLockedOutAsync(user))
            return Denied(user, "locked-out");

        if (userManager.Options.SignIn.RequireConfirmedEmail && !await userManager.IsEmailConfirmedAsync(user))
            return Denied(user, "email-not-confirmed");

        if (!await SyncRolesAsync(user, external, settings))
            return Denied(user, "role-sync-failed");

        return new PasswordGrantResult
        {
            Success = true,
            Subject = await userManager.GetUserIdAsync(user),
            UserName = await userManager.GetUserNameAsync(user),
            TenantId = user.TenantId,
            Email = await userManager.GetEmailAsync(user),
            Roles = (await userManager.GetRolesAsync(user)).ToList(),
            SecurityStamp = await userManager.GetSecurityStampAsync(user),
        };
    }

    /// <summary>Finds the account the subject is linked to, links an existing one by verified email, or creates one.</summary>
    private async Task<(TUser? User, string? Reason)> ResolveAccountAsync(
        FederatedIdentity external, FederatedLoginOptions settings)
    {
        var linked = await userManager.FindByLoginAsync(settings.ProviderName, external.Subject);
        if (linked is not null)
            return (linked, null);

        if (external.Email is not null)
        {
            var sameEmail = await userManager.FindByEmailAsync(external.Email);
            if (sameEmail is not null)
            {
                if (!(settings.LinkByVerifiedEmail && external.EmailVerified))
                    return (null, "email-in-use");

                var added = await userManager.AddLoginAsync(sameEmail, LoginFor(external, settings));
                return added.Succeeded ? (sameEmail, null) : (null, "link-failed");
            }
        }

        if (!settings.CreateUnknownUsers)
            return (null, "no-account");

        return await CreateAccountAsync(external, settings);
    }

    private async Task<(TUser? User, string? Reason)> CreateAccountAsync(FederatedIdentity external, FederatedLoginOptions settings)
    {
        // The email is the user name when there is one (it is unique and readable). Without it, a hash of the subject
        // stands in: provider subjects can hold characters a user name may not.
        var userName = external.Email ?? $"{settings.ProviderName}-{Hash(external.Subject)}";
        if (await userManager.FindByNameAsync(userName) is not null)
            return (null, "user-name-taken");

        var (first, last) = SplitName(external.Name);
        var user = new TUser
        {
            UserName = userName,
            Email = external.Email,
            EmailConfirmed = external.EmailVerified,
            FirstName = first,
            LastName = last,
            IsActive = true,
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            return (null, "create-failed");

        var login = await userManager.AddLoginAsync(user, LoginFor(external, settings));
        if (login.Succeeded)
            return (user, null);

        // Never leave an account behind that the provider cannot reach.
        await userManager.DeleteAsync(user);
        return (null, "link-failed");
    }

    /// <summary>
    /// Brings the account's roles in line with the provider's groups. Adds mapped roles the user lacks (skipping role names
    /// that do not exist locally) and removes mapped roles the user no longer has in the provider. Returns false when a
    /// change fails.
    /// </summary>
    private async Task<bool> SyncRolesAsync(TUser user, FederatedIdentity external, FederatedLoginOptions settings)
    {
        var managed = settings.RoleMap.Values.Concat(settings.DefaultRoles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = external.LocalRoles.Concat(settings.DefaultRoles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = (await userManager.GetRolesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<string>();
        foreach (var role in desired)
        {
            if (!current.Contains(role) && await roleManager.RoleExistsAsync(role))
                toAdd.Add(role);
        }

        var toRemove = current.Where(role => managed.Contains(role) && !desired.Contains(role)).ToList();

        if (toRemove.Count > 0 && !(await userManager.RemoveFromRolesAsync(user, toRemove)).Succeeded)
            return false;

        return toAdd.Count == 0 || (await userManager.AddToRolesAsync(user, toAdd)).Succeeded;
    }

    private static UserLoginInfo LoginFor(FederatedIdentity external, FederatedLoginOptions settings)
        => new(settings.ProviderName, external.Subject, settings.ProviderName);

    private static (string? First, string? Last) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, null);

        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private PasswordGrantResult Denied(TUser? user, string reason)
    {
        audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "signin.federated",
            Outcome = SecurityAuditOutcomes.Denied,
            TenantId = user?.TenantId,
            Actor = user?.Id.ToString(),
            Details = new Dictionary<string, string?> { ["reason"] = reason },
        });
        return PasswordGrantResult.Denied();
    }
}
