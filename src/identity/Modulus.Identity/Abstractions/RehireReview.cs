namespace Modulus.Identity.Abstractions;

/// <summary>Who may approve bringing an archived account back. Bound from <c>Identity:RehireReview</c>.</summary>
public sealed class RehireReviewOptions
{
    /// <summary>
    /// The roles whose holders may approve a rehire, in the account's own company. Default: the tenant administrator and HR.
    /// </summary>
    public List<string> ApproverRoles { get; set; } = ["TenantAdministrator", "HR"];
}

/// <summary>
/// The reviewed-rehire step (A1-1). Approval is the only way to produce a reviewed rehire: an approver in the account's company
/// confirms the archived account may return, and the account moves to <see cref="AccountLifecycle.PendingActivation"/>. It still
/// has to accept its link before it can sign in.
/// </summary>
public interface IRehireReviewService
{
    /// <summary>Approves the rehire of an archived account and returns its new state.</summary>
    /// <param name="userId">The archived account.</param>
    /// <param name="approverId">The person approving. Must hold an approver role in the account's company and must not be the account.</param>
    /// <param name="reason">Why the account returns. Kept in the transition history.</param>
    /// <param name="ct">Cancels the change.</param>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.NotFoundException">No account has that identifier.</exception>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.ConflictException">The account is not archived.</exception>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.ForbiddenException">The approver is the account, is in another company, or holds no approver role.</exception>
    /// <exception cref="ArgumentException">The reason is empty.</exception>
    Task<AccountLifecycle> ApproveAsync(Guid userId, Guid approverId, string reason, CancellationToken ct = default);
}
