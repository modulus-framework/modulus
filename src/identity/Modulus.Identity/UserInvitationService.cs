using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>One person to invite.</summary>
/// <param name="Email">Their e-mail address (also their user name when <paramref name="UserName"/> is null).</param>
/// <param name="Roles">The roles they start with; each must exist.</param>
/// <param name="TenantId">The company the account belongs to; null for a host-level account.</param>
/// <param name="FirstName">Optional first name.</param>
/// <param name="LastName">Optional last name.</param>
/// <param name="UserName">Optional user name.</param>
public sealed record UserInvitation(
    string Email, IReadOnlyCollection<string>? Roles = null, Guid? TenantId = null, string? FirstName = null, string? LastName = null, string? UserName = null);

/// <summary>The outcome of one invitation.</summary>
/// <param name="Email">The address invited.</param>
/// <param name="UserId">The account created (or re-invited), or null when it failed.</param>
/// <param name="Error">Why it failed, or null.</param>
public sealed record InvitationResult(string Email, Guid? UserId, string? Error)
{
    /// <summary>Whether the invitation was sent.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Invites people: creates the account without a password (it cannot sign in) and mails a one-time link to set one
/// (<c>POST account/accept-invitation</c>). Also the engine of a bulk import: parse the CSV or spreadsheet and call
/// <see cref="InviteManyAsync"/>.
/// </summary>
public interface IUserInvitationService
{
    /// <summary>Invites one person. An address whose account has not accepted yet is sent a fresh link (roles are added, never removed).</summary>
    Task<InvitationResult> InviteAsync(UserInvitation invitation, CancellationToken ct = default);

    /// <summary>Invites many people; one failure does not stop the rest. Results are in input order.</summary>
    Task<IReadOnlyList<InvitationResult>> InviteManyAsync(IEnumerable<UserInvitation> invitations, CancellationToken ct = default);
}

internal sealed class UserInvitationService<TUser>(
    UserManager<TUser> users,
    RoleManager<ModulusRole> roles,
    IIdentityEmailSender sender,
    IServiceProvider services,
    ILogger<UserInvitationService<TUser>> logger) : IUserInvitationService
    where TUser : ModulusUser, new()
{
    public async Task<IReadOnlyList<InvitationResult>> InviteManyAsync(IEnumerable<UserInvitation> invitations, CancellationToken ct = default)
    {
        var results = new List<InvitationResult>();
        foreach (var invitation in invitations)
            results.Add(await InviteAsync(invitation, ct));
        return results;
    }

    public async Task<InvitationResult> InviteAsync(UserInvitation invitation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        var email = invitation.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal))
            return new InvitationResult(invitation.Email ?? "", null, "A valid e-mail address is required.");

        foreach (var role in invitation.Roles ?? [])
        {
            if (!await roles.RoleExistsAsync(role))
                return new InvitationResult(email, null, $"The role '{role}' does not exist.");
        }

        var user = await users.FindByEmailAsync(email);
        if (user is not null)
        {
            // Someone who already has a password has accepted: an invitation must not mail a reset link to an active account.
            if (await users.HasPasswordAsync(user))
                return new InvitationResult(email, user.Id, "This person already has an account.");
        }
        else
        {
            user = new TUser
            {
                UserName = string.IsNullOrWhiteSpace(invitation.UserName) ? email : invitation.UserName.Trim(),
                Email = email,
                TenantId = invitation.TenantId,
                FirstName = invitation.FirstName,
                LastName = invitation.LastName,
                IsActive = true,
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return new InvitationResult(email, null, string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var missing = new List<string>();
        foreach (var role in invitation.Roles ?? [])
        {
            if (!await users.IsInRoleAsync(user, role))
                missing.Add(role);
        }

        if (missing.Count > 0)
        {
            var added = await users.AddToRolesAsync(user, missing);
            if (!added.Succeeded)
                return new InvitationResult(email, user.Id, string.Join(" ", added.Errors.Select(e => e.Description)));
        }

        await sender.SendInvitationEmailAsync(email, await users.GeneratePasswordResetTokenAsync(user), ct);
        services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "user.invited",
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = user.TenantId,
            Target = $"user:{user.Id}",
        });
        logger.LogInformation("Invited user {UserId}.", user.Id);
        return new InvitationResult(email, user.Id, null);
    }
}
