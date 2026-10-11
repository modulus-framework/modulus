namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// The password grant for people who live in a directory (LDAP / Active Directory). It finds the person with a search account,
/// then proves the password by binding as that person, so no password is stored locally. A user name no directory knows goes to
/// the wrapped validator (local passwords) when <see cref="LdapOptions.AllowLocalFallback"/> is on; a user name a directory
/// knows never does. An unreachable directory refuses the sign-in (it never falls back to a local password).
/// </summary>
internal sealed class LdapPasswordGrantValidator<TUser>(
    IPasswordGrantCredentialValidator inner,
    ILdapDirectoryClient directory,
    ExternalAccountProvisioner<TUser> provisioner,
    UserManager<TUser> users,
    IOptions<LdapOptions> options,
    IServiceProvider services,
    ILogger<LdapPasswordGrantValidator<TUser>> logger)
    : IPasswordGrantCredentialValidator
    where TUser : ModulusUser, new()
{
    public Task<PasswordGrantResult> ValidateAsync(string username, string password, CancellationToken ct = default)
        => ValidateWithSecondFactorAsync(username, password, null, ct);

    public async Task<PasswordGrantResult> ValidateWithSecondFactorAsync(
        string username, string password, string? verificationCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return PasswordGrantResult.Denied(); // an empty password would be an anonymous (successful) LDAP bind

        var config = options.Value;
        LdapDirectoryOptions? source = null;
        LdapUserEntry? entry = null;
        try
        {
            foreach (var candidate in config.Directories)
            {
                entry = await directory.FindUserAsync(candidate, username.Trim(), ct);
                if (entry is not null)
                {
                    source = candidate;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "A directory could not be searched; the sign-in is refused.");
            Audit(null, "directory-unavailable");
            return PasswordGrantResult.Denied();
        }

        if (source is null || entry is null)
        {
            return config.AllowLocalFallback
                ? await inner.ValidateWithSecondFactorAsync(username, password, verificationCode, ct)
                : PasswordGrantResult.Denied();
        }

        bool bound;
        try
        {
            bound = await directory.BindAsync(source, entry.DistinguishedName, password, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The directory {Directory} could not be reached; the sign-in is refused.", source.Name);
            Audit(null, "directory-unavailable");
            return PasswordGrantResult.Denied();
        }

        if (!bound)
        {
            Audit(source.TenantId, "wrong-password");
            return PasswordGrantResult.Denied();
        }

        var subject = entry.First(source.SubjectAttribute) ?? entry.DistinguishedName;
        var person = new ExternalPrincipal(
            $"ldap:{source.Name}",
            subject,
            entry.First(source.UserNameAttribute) ?? username.Trim(),
            entry.First(source.EmailAttribute),
            entry.First(source.FirstNameAttribute),
            entry.First(source.LastNameAttribute),
            entry.All(source.GroupAttribute));
        var rules = new ExternalProvisioningRules(source.TenantId, source.AutoProvision, source.LinkExistingByEmail, source.GroupRoles);

        var outcome = await provisioner.SignInAsync(person, rules, ct);
        if (outcome.User is not { } user)
        {
            Audit(source.TenantId, outcome.Denial ?? "denied");
            return PasswordGrantResult.Denied();
        }

        // A second factor the account holds locally still applies: the directory password alone is one factor.
        var secondFactor = false;
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(verificationCode))
                return PasswordGrantResult.MfaRequired();
            var digits = verificationCode.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
            if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, digits)
                && !(await users.RedeemTwoFactorRecoveryCodeAsync(user, verificationCode.Trim())).Succeeded)
            {
                await users.AccessFailedAsync(user);
                Audit(source.TenantId, "wrong-second-factor");
                return PasswordGrantResult.Denied();
            }

            secondFactor = true;
        }

        return new PasswordGrantResult
        {
            Success = true,
            Subject = await users.GetUserIdAsync(user),
            UserName = await users.GetUserNameAsync(user),
            TenantId = user.TenantId,
            Email = await users.GetEmailAsync(user),
            Roles = [.. await users.GetRolesAsync(user)],
            SecurityStamp = await users.GetSecurityStampAsync(user),
            SecondFactorVerified = secondFactor,
        };
    }

    private void Audit(Guid? tenantId, string reason)
        => services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "signin.directory.denied",
            Outcome = SecurityAuditOutcomes.Denied,
            TenantId = tenantId,
            Details = new Dictionary<string, string?> { ["reason"] = reason },
        });
}
