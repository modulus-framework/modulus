namespace Modulus.Authorization.Workflow;

using Microsoft.Extensions.Logging;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Multi-step approval of documents. A chain (data) lists the steps for a document type and amount; each step is open to the
/// holders of a permission, within their "up to this amount" authority. Rules the engine enforces on every decision: the requester
/// never approves their own document, one person acts on only one step of a request, a step needing several approvals needs
/// different people, and two approvers racing for the same step cannot both win.
/// </summary>
public interface IApprovalWorkflow
{
    /// <summary>
    /// Submits a document. Call it from the app's own handler with the amount read from the stored document, never from request
    /// input. A document with a pending request cannot be submitted again.
    /// </summary>
    Task<ApprovalRequest> SubmitAsync(ApprovalSubmission submission, CancellationToken ct = default);

    /// <summary>One request, or null.</summary>
    Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Approves the current step as the signed-in user.</summary>
    Task<ApprovalRequest> ApproveAsync(Guid id, string? comment = null, CancellationToken ct = default);

    /// <summary>Refuses the request as the signed-in user (a comment is required).</summary>
    Task<ApprovalRequest> RejectAsync(Guid id, string comment, CancellationToken ct = default);

    /// <summary>Withdraws the request; only its requester, while it is pending.</summary>
    Task<ApprovalRequest> CancelAsync(Guid id, string? comment = null, CancellationToken ct = default);

    /// <summary>Pending requests the signed-in user could decide now (permission, authority, not their own, not a step they already took).</summary>
    Task<IReadOnlyList<ApprovalRequest>> InboxAsync(CancellationToken ct = default);

    /// <summary>Requests the signed-in user submitted.</summary>
    Task<IReadOnlyList<ApprovalRequest>> MineAsync(CancellationToken ct = default);

    /// <summary>The history of a document's requests.</summary>
    Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default);

    /// <summary>Why the signed-in user could not decide the request now (a reason code), or null when they can.</summary>
    Task<string?> WhyNotAsync(Guid id, CancellationToken ct = default);

    /// <summary>Escalates every step that waited longer than its limit; returns how many. Run by the background service.</summary>
    Task<int> EscalateOverdueAsync(CancellationToken ct = default);
}

/// <summary>Reason codes of approval decisions (also the <c>code</c> of the refusal).</summary>
public static class ApprovalReasonCodes
{
    /// <summary>The requester tried to decide their own request.</summary>
    public const string RequesterCannotApprove = "REQUESTER_CANNOT_APPROVE";

    /// <summary>The user already acted on this request.</summary>
    public const string AlreadyActed = "ALREADY_ACTED";

    /// <summary>The user does not hold the step's permission.</summary>
    public const string NotAnApprover = "NOT_AN_APPROVER";

    /// <summary>The request is not pending any more.</summary>
    public const string NotPending = "NOT_PENDING";
}

internal sealed class ApprovalWorkflow(
    IApprovalChainStore chains,
    IApprovalRequestStore requests,
    IApprovalAuthorityEvaluator authority,
    IPrincipalGrantQuerySource principal,
    ICurrentUser user,
    ICurrentTenant tenant,
    IEnumerable<IApprovalWorkflowObserver> observers,
    ILogger<ApprovalWorkflow> logger,
    TimeProvider? clock = null,
    ISecurityAuditLog? audit = null) : IApprovalWorkflow
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ApprovalRequest> SubmitAsync(ApprovalSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var requester = RequireUser();
        if (string.IsNullOrWhiteSpace(submission.DocumentType) || string.IsNullOrWhiteSpace(submission.DocumentId))
            throw new ValidationException(["The document type and id are required."]);
        if (submission.Amount < 0)
            throw new ValidationException(["The amount cannot be negative."]);

        var existing = await requests.ForDocumentAsync(submission.DocumentType, submission.DocumentId, ct);
        if (existing.Any(r => r.Status == ApprovalStatus.Pending))
            throw new ConflictException("This document already has a request waiting for approval.");

        var chain = await ChooseChainAsync(submission, ct)
            ?? throw new ValidationException([$"No approval chain covers a {submission.DocumentType} of {submission.Amount} {submission.Currency}."]);
        var now = _clock.GetUtcNow();
        var request = new ApprovalRequest(
            Guid.NewGuid(), tenant.TenantId, chain.Id, submission.DocumentType.Trim(), submission.DocumentId, submission.Amount,
            submission.Currency?.Trim().ToUpperInvariant(), submission.OrgUnitId, requester, submission.Summary,
            chain.Steps, 0, ApprovalStatus.Pending, [], now, now, false, 1);
        await requests.AddAsync(request, ct);
        Record("approval.submitted", request);
        await NotifyAsync(request, ApprovalChanges.Submitted, ct);
        return request;
    }

    public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default) => requests.GetAsync(id, ct);

    public Task<ApprovalRequest> ApproveAsync(Guid id, string? comment = null, CancellationToken ct = default)
        => DecideAsync(id, ApprovalDecisionKind.Approved, comment, ct);

    public Task<ApprovalRequest> RejectAsync(Guid id, string comment, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(comment)
            ? throw new ValidationException(["A reason is required to reject a request."])
            : DecideAsync(id, ApprovalDecisionKind.Rejected, comment, ct);

    public async Task<ApprovalRequest> CancelAsync(Guid id, string? comment = null, CancellationToken ct = default)
    {
        var caller = RequireUser();
        var request = await LoadPendingAsync(id, ct);
        if (request.RequestedBy != caller)
            throw new ForbiddenException("workflow:cancel", "Only the requester can withdraw a request.");

        var updated = request with
        {
            Status = ApprovalStatus.Cancelled,
            CompletedAt = _clock.GetUtcNow(),
            Version = request.Version + 1,
        };
        await SaveAsync(updated, request.Version, ct);
        Record("approval.cancelled", updated);
        await NotifyAsync(updated, ApprovalChanges.Cancelled, ct);
        return updated;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> InboxAsync(CancellationToken ct = default)
    {
        var caller = RequireUser();
        var inbox = new List<ApprovalRequest>();
        foreach (var request in await requests.PendingAsync(ct))
        {
            if (Eligibility(request, caller).Code is null)
                inbox.Add(request);
        }

        return inbox;
    }

    public Task<IReadOnlyList<ApprovalRequest>> MineAsync(CancellationToken ct = default) => requests.RequestedByAsync(RequireUser(), ct);

    public Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default)
        => requests.ForDocumentAsync(documentType, documentId, ct);

    public async Task<string?> WhyNotAsync(Guid id, CancellationToken ct = default)
    {
        var caller = RequireUser();
        var request = await requests.GetAsync(id, ct) ?? throw new NotFoundException("The approval request does not exist.");
        return request.Status != ApprovalStatus.Pending ? ApprovalReasonCodes.NotPending : Eligibility(request, caller).Code;
    }

    public async Task<int> EscalateOverdueAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var escalated = 0;
        foreach (var request in await requests.PendingAsync(ct))
        {
            if (request.Escalated || request.Step is not { EscalateAfter: { } after, EscalationPermission: not null } || now - request.StepStartedAt < after)
                continue;

            var updated = request with { Escalated = true, Version = request.Version + 1 };
            if (!await requests.TryUpdateAsync(updated, request.Version, ct))
                continue; // decided meanwhile

            escalated++;
            Record("approval.escalated", updated);
            await NotifyAsync(updated, ApprovalChanges.Escalated, ct);
        }

        return escalated;
    }

    private async Task<ApprovalRequest> DecideAsync(Guid id, ApprovalDecisionKind kind, string? comment, CancellationToken ct)
    {
        var caller = RequireUser();
        var request = await LoadPendingAsync(id, ct);
        var (code, permission) = Eligibility(request, caller);
        if (code is not null)
            throw Refusal(code, permission ?? request.Step!.Permission);

        var now = _clock.GetUtcNow();
        var decision = new ApprovalDecision(request.CurrentStep, caller, kind, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), now, permission!);
        var decisions = new List<ApprovalDecision>(request.Decisions) { decision };

        ApprovalRequest updated;
        string change;
        if (kind == ApprovalDecisionKind.Rejected)
        {
            updated = request with { Decisions = decisions, Status = ApprovalStatus.Rejected, CompletedAt = now, Version = request.Version + 1 };
            change = ApprovalChanges.Rejected;
        }
        else
        {
            var approvals = decisions.Count(d => d.Step == request.CurrentStep && d.Kind == ApprovalDecisionKind.Approved);
            if (approvals < request.Step!.RequiredApprovals)
            {
                updated = request with { Decisions = decisions, Version = request.Version + 1 };
                change = ApprovalChanges.ApprovalRecorded;
            }
            else if (request.CurrentStep + 1 < request.Steps.Count)
            {
                updated = request with
                {
                    Decisions = decisions,
                    CurrentStep = request.CurrentStep + 1,
                    StepStartedAt = now,
                    Escalated = false,
                    Version = request.Version + 1,
                };
                change = ApprovalChanges.StepApproved;
            }
            else
            {
                updated = request with
                {
                    Decisions = decisions,
                    CurrentStep = request.Steps.Count,
                    Status = ApprovalStatus.Approved,
                    CompletedAt = now,
                    Version = request.Version + 1,
                };
                change = ApprovalChanges.Approved;
            }
        }

        await SaveAsync(updated, request.Version, ct);
        Record($"approval.{(kind == ApprovalDecisionKind.Rejected ? "rejected" : "approved")}", updated, request.CurrentStep);
        await NotifyAsync(updated, change, ct);
        return updated;
    }

    /// <summary>Whether <paramref name="caller"/> may decide the current step: null code means yes; permission is the one they would act under.</summary>
    private (string? Code, string? Permission) Eligibility(ApprovalRequest request, Guid caller)
    {
        if (request.Step is not { } step)
            return (ApprovalReasonCodes.NotPending, null);
        if (request.RequestedBy == caller)
            return (ApprovalReasonCodes.RequesterCannotApprove, null);
        if (request.Decisions.Any(d => d.UserId == caller))
            return (ApprovalReasonCodes.AlreadyActed, null);

        var context = new ApprovalContext(request.Amount, request.Currency, request.DocumentType, request.OrgUnitId);
        string? failure = null;
        foreach (var permission in Permissions(step, request.Escalated))
        {
            if (!user.HasPermission(permission))
                continue;
            if (!step.CheckAuthority)
                return (null, permission);

            var check = authority.Check(principal.Current, permission, context);
            if (check.IsWithinAuthority)
                return (null, permission);
            failure ??= check.ReasonCode;
        }

        return (failure ?? ApprovalReasonCodes.NotAnApprover, null);
    }

    private static IEnumerable<string> Permissions(ApprovalStep step, bool escalated)
    {
        yield return step.Permission;
        if (escalated && step.EscalationPermission is { } escalation)
            yield return escalation;
    }

    private static ForbiddenException Refusal(string code, string permission)
        => new(permission, code switch
        {
            ApprovalReasonCodes.RequesterCannotApprove => $"{code}: nobody approves their own request.",
            ApprovalReasonCodes.AlreadyActed => $"{code}: you already acted on this request.",
            AccessReasonCodes.ApprovalLimitExceeded => $"{code}: the amount is above your approval limit.",
            AccessReasonCodes.NoApprovalAuthority => $"{code}: you have no approval limit for this document.",
            _ => $"{code}: you are not an approver of this step.",
        });

    private async Task<ApprovalRequest> LoadPendingAsync(Guid id, CancellationToken ct)
    {
        var request = await requests.GetAsync(id, ct) ?? throw new NotFoundException("The approval request does not exist.");
        return request.Status == ApprovalStatus.Pending
            ? request
            : throw new ConflictException($"{ApprovalReasonCodes.NotPending}: the request is already {request.Status.ToString().ToLowerInvariant()}.");
    }

    private async Task SaveAsync(ApprovalRequest updated, long expectedVersion, CancellationToken ct)
    {
        if (!await requests.TryUpdateAsync(updated, expectedVersion, ct))
            throw new ConflictException("The request was changed by someone else at the same time. Reload it and decide again.");
    }

    private async Task<ApprovalChain?> ChooseChainAsync(ApprovalSubmission submission, CancellationToken ct)
    {
        var currency = submission.Currency?.Trim();
        return (await chains.ListAsync(ct))
            .Where(c => c.IsActive
                && string.Equals(c.DocumentType, submission.DocumentType.Trim(), StringComparison.OrdinalIgnoreCase)
                && (c.Currency is null || string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase))
                && (c.MinAmount is null || submission.Amount >= c.MinAmount)
                && (c.MaxAmount is null || submission.Amount <= c.MaxAmount)
                && c.Steps.Count > 0)
            // The most specific band wins: a currency-specific chain, then the highest lower bound.
            .OrderByDescending(c => c.Currency is not null)
            .ThenByDescending(c => c.MinAmount ?? 0m)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private Guid RequireUser()
        => user is { IsAuthenticated: true, UserId: { } id } ? id : throw new UnauthorizedException();

    private void Record(string action, ApprovalRequest request, int? step = null)
        => audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Authorization,
            Action = action,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = request.TenantId,
            Actor = user.UserId?.ToString(),
            Target = $"approval:{request.Id:N} {request.DocumentType}:{request.DocumentId}" + (step is { } s ? $" step:{s}" : ""),
        });

    private async ValueTask NotifyAsync(ApprovalRequest request, string change, CancellationToken ct)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnChangedAsync(request, change, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "An approval observer failed for request {RequestId} ({Change}).", request.Id, change);
            }
        }
    }
}
