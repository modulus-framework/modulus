namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Identity.Abstractions;

/// <summary>A person an outside system (a directory, a SAML identity provider) has just authenticated.</summary>
/// <param name="Provider">Which system, for example <c>ldap:hq</c> or <c>saml:okta</c>; with <paramref name="Subject"/> it identifies the person.</param>
/// <param name="Subject">The person's stable id in that system (never changes, unlike a name or an address).</param>
/// <param name="UserName">The wanted user name for a new account; falls back to the e-mail address.</param>
/// <param name="Email">The e-mail address.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Surname.</param>
/// <param name="Groups">The groups the system says the person belongs to.</param>
/// <param name="EmailVerified">The system vouches that <paramref name="Email"/> belongs to the person; only then may it link to an existing account.</param>
internal sealed record ExternalPrincipal(
    string Provider, string Subject, string? UserName, string? Email, string? FirstName, string? LastName, IReadOnlyCollection<string> Groups,
    bool EmailVerified = false);

/// <summary>How a system's people become accounts.</summary>
internal sealed record ExternalProvisioningRules(
    Guid? TenantId, bool AutoProvision, bool LinkExistingByEmail, IReadOnlyDictionary<string, string> GroupRoles);

/// <summary>The result of letting an authenticated outsider in: the account, or why not.</summary>
internal sealed record ExternalSignIn<TUser>(TUser? User, string? Denial) where TUser : ModulusUser
{
    public bool Succeeded => User is not null;
}

/// <summary>
/// Turns "the directory / the identity provider says this person is who they claim" into a local account: finds the linked one,
/// or links an existing one by e-mail (when allowed), or creates one (when allowed, taking a seat of the company), then brings
/// the profile and the mapped roles up to date. The account has no password: the outside system is its only way in.
/// </summary>
internal sealed class ExternalAccountProvisioner<TUser>(
    UserManager<TUser> users,
    IServiceProvider services,
    ILogger<ExternalAccountProvisioner<TUser>> logger)
    where TUser : ModulusUser, new()
{
    public async Task<ExternalSignIn<TUser>> SignInAsync(ExternalPrincipal person, ExternalProvisioningRules rules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(rules);

        var user = await users.FindByLoginAsync(person.Provider, person.Subject);
        var created = false;
        if (user is null)
        {
            var sameAddress = string.IsNullOrWhiteSpace(person.Email) ? null : await users.FindByEmailAsync(person.Email);
            if (sameAddress is not null)
            {
                // A local account already holds this address. It is linked only when the system verified the address and the
                // rules allow linking; otherwise the sign-in is refused rather than creating a second account for the address.
                if (!(rules.LinkExistingByEmail && person.EmailVerified && sameAddress.TenantId == rules.TenantId))
                    return Deny("email-in-use");

                user = sameAddress;
                var link = await users.AddLoginAsync(user, new UserLoginInfo(person.Provider, person.Subject, person.Provider));
                if (!link.Succeeded)
                    return Deny("link-failed");
            }
            else if (rules.AutoProvision)
            {
                var made = await CreateAsync(person, rules, ct);
                if (made.User is null)
                    return made;
                user = made.User;
                created = true;
            }
            else
            {
                return Deny("not-provisioned");
            }
        }

        if (!user.MayStartNewAccess())
            return Deny("account-disabled");
        if (await users.IsLockedOutAsync(user))
            return Deny("locked-out");

        if (!created)
            await RefreshProfileAsync(user, person);
        await SyncRolesAsync(user, person.Groups, rules.GroupRoles);
        return new ExternalSignIn<TUser>(user, null);
    }

    private async Task<ExternalSignIn<TUser>> CreateAsync(ExternalPrincipal person, ExternalProvisioningRules rules, CancellationToken ct)
    {
        var name = (person.UserName ?? person.Email)?.Trim();
        if (string.IsNullOrEmpty(name))
            return Deny("no-user-name");

        var user = new TUser
        {
            UserName = name,
            Email = person.Email?.Trim(),
            // The outside system vouches for the address; there is no first-password link to prove it.
            EmailConfirmed = !string.IsNullOrWhiteSpace(person.Email),
            FirstName = person.FirstName,
            LastName = person.LastName,
            TenantId = rules.TenantId,
        };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
        {
            logger.LogWarning("Could not create the account for {Provider}: {Errors}", person.Provider, string.Join(" ", result.Errors.Select(e => e.Code)));
            return Deny("create-failed");
        }

        var linked = await users.AddLoginAsync(user, new UserLoginInfo(person.Provider, person.Subject, person.Provider));
        if (!linked.Succeeded)
        {
            await users.DeleteAsync(user);
            return Deny("link-failed");
        }

        if (rules.TenantId is { } company && services.GetService<ITenantMemberProvisioner>() is { } provisioner)
        {
            try
            {
                await provisioner.AddMemberAsync(user.Id, company, ct);
            }
            catch (ConflictException)
            {
                await users.DeleteAsync(user);
                return Deny("seat-limit");
            }
        }

        services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "external.account.created",
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = user.TenantId,
            Target = $"user:{user.Id}",
        });
        return new ExternalSignIn<TUser>(user, null);
    }

    private async Task RefreshProfileAsync(TUser user, ExternalPrincipal person)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(person.FirstName) && user.FirstName != person.FirstName)
        {
            user.FirstName = person.FirstName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(person.LastName) && user.LastName != person.LastName)
        {
            user.LastName = person.LastName;
            changed = true;
        }

        if (changed)
            await users.UpdateAsync(user);
    }

    private async Task SyncRolesAsync(TUser user, IReadOnlyCollection<string> groups, IReadOnlyDictionary<string, string> groupRoles)
    {
        if (groupRoles.Count == 0)
            return;

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var key in GroupKeys(group))
            {
                if (groupRoles.TryGetValue(key, out var role))
                    wanted.Add(role);
            }
        }

        var managed = new HashSet<string>(groupRoles.Values, StringComparer.OrdinalIgnoreCase);
        var has = (await users.GetRolesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var add = wanted.Where(r => !has.Contains(r)).ToList();
        var remove = has.Where(r => managed.Contains(r) && !wanted.Contains(r)).ToList();
        foreach (var role in add)
        {
            try
            {
                var result = await users.AddToRoleAsync(user, role);
                if (!result.Succeeded)
                    logger.LogWarning("Could not give role {Role} to a directory user: {Errors}", role, string.Join(" ", result.Errors.Select(e => e.Code)));
            }
            catch (InvalidOperationException)
            {
                // The mapped role does not exist; a misspelt mapping must not stop the sign-in or create privileged roles.
                logger.LogWarning("The role {Role} in the group mapping does not exist and was skipped.", role);
            }
        }

        if (remove.Count > 0)
            await users.RemoveFromRolesAsync(user, remove);
    }

    /// <summary>The names a group can be matched by: its full value, and the CN of a distinguished name.</summary>
    internal static IEnumerable<string> GroupKeys(string group)
    {
        yield return group;
        if (group.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var rest = group[3..];
            var end = -1;
            for (var i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '\\')
                    i++;
                else if (rest[i] == ',')
                {
                    end = i;
                    break;
                }
            }

            yield return (end < 0 ? rest : rest[..end]).Replace("\\,", ",", StringComparison.Ordinal);
        }
    }

    private static ExternalSignIn<TUser> Deny(string reason) => new(null, reason);
}
