using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// Approves a rehire by an approver in the account's company. The state change goes through <see cref="IAccountLifecycleService"/>,
/// so the transition, its actor and reason, and the audit event are recorded the same way as any other change.
/// </summary>
internal sealed class RehireReviewService<TUser>(
    UserManager<TUser> users,
    IAccountLifecycleService lifecycle,
    IOptions<RehireReviewOptions> options,
    ISecurityAuditLog? audit = null) : IRehireReviewService
    where TUser : ModulusUser
{
    private const string Permission = "identity:accounts:rehire-approve";

    public async Task<AccountLifecycle> ApproveAsync(Guid userId, Guid approverId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (userId == approverId)
            throw new ForbiddenException(Permission, "An account cannot approve its own rehire.");

        var account = await users.FindByIdAsync(userId.ToString("D"))
            ?? throw new NotFoundException("No account has that identifier.");
        if (account.Lifecycle != AccountLifecycle.Archived)
            throw new ConflictException("Only an archived account is rehired.");

        var approver = await users.FindByIdAsync(approverId.ToString("D"))
            ?? throw new ForbiddenException(Permission, "The approver is not an account.");
        if (approver.TenantId != account.TenantId)
            throw new ForbiddenException(Permission, "The approver is not in the account's company.");

        var roles = await users.GetRolesAsync(approver);
        if (!roles.Intersect(options.Value.ApproverRoles, StringComparer.OrdinalIgnoreCase).Any())
            throw new ForbiddenException(Permission, "The approver does not hold a rehire approver role.");

        var state = await lifecycle.ChangeAsync(
            new AccountLifecycleChange(userId, AccountLifecycle.PendingActivation, approverId, reason.Trim(), RehireReviewed: true), ct);

        audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "account.rehire_approved",
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = account.TenantId,
            Actor = approverId.ToString(),
            Target = $"user:{userId}",
        });
        return state;
    }
}
