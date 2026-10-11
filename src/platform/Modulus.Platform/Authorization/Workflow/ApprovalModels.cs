namespace Modulus.Authorization.Workflow;

/// <summary>
/// One step of an approval chain. Whoever holds <see cref="Permission"/> (directly, through a role, a position or a delegation)
/// may act on it, within their approval authority for the document's amount.
/// </summary>
/// <param name="Name">What the step is called (for example <c>Finance review</c>).</param>
/// <param name="Permission">The permission an approver needs.</param>
/// <param name="RequiredApprovals">How many different people must approve before the chain moves on.</param>
/// <param name="EscalateAfter">After this long without a decision the step is escalated; null never escalates.</param>
/// <param name="EscalationPermission">Once escalated, holders of this permission may also act on the step. Needed when <paramref name="EscalateAfter"/> is set.</param>
/// <param name="CheckAuthority">Whether the approver's "up to this amount" limit for the permission must cover the document.</param>
public sealed record ApprovalStep(
    string Name,
    string Permission,
    int RequiredApprovals = 1,
    TimeSpan? EscalateAfter = null,
    string? EscalationPermission = null,
    bool CheckAuthority = true);

/// <summary>
/// The steps documents of one type go through, chosen by amount. Configuration data: change a chain and new requests follow it,
/// while requests already under way keep the steps they started with.
/// </summary>
/// <param name="Id">The chain id.</param>
/// <param name="Name">A name for people.</param>
/// <param name="DocumentType">The document type it applies to (the app's name for it, for example <c>PurchaseOrder</c>).</param>
/// <param name="MinAmount">The smallest amount it applies to (inclusive); null from zero.</param>
/// <param name="MaxAmount">The largest amount it applies to (inclusive); null without end.</param>
/// <param name="Currency">The currency of the bounds; null applies to any currency.</param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="IsActive">Inactive chains are never chosen.</param>
public sealed record ApprovalChain(
    Guid Id,
    string Name,
    string DocumentType,
    decimal? MinAmount,
    decimal? MaxAmount,
    string? Currency,
    IReadOnlyList<ApprovalStep> Steps,
    bool IsActive = true);

/// <summary>Where a request stands.</summary>
public enum ApprovalStatus
{
    /// <summary>Waiting for approvers.</summary>
    Pending = 0,

    /// <summary>Every step approved.</summary>
    Approved = 1,

    /// <summary>An approver refused it.</summary>
    Rejected = 2,

    /// <summary>The requester withdrew it.</summary>
    Cancelled = 3,
}

/// <summary>What an approver decided.</summary>
public enum ApprovalDecisionKind
{
    /// <summary>The step is approved.</summary>
    Approved = 0,

    /// <summary>The whole request is refused.</summary>
    Rejected = 1,
}

/// <summary>One decision on a request.</summary>
/// <param name="Step">The step (zero based) it was made on.</param>
/// <param name="UserId">Who decided.</param>
/// <param name="Kind">Approved or rejected.</param>
/// <param name="Comment">The approver's note.</param>
/// <param name="At">When.</param>
/// <param name="Permission">The permission the approver acted under (the step's, or its escalation permission).</param>
public sealed record ApprovalDecision(int Step, Guid UserId, ApprovalDecisionKind Kind, string? Comment, DateTimeOffset At, string Permission);

/// <summary>A document going through a chain. The amount, steps and trail are fixed when it is submitted.</summary>
/// <param name="Id">The request id.</param>
/// <param name="TenantId">The company, or null for the host.</param>
/// <param name="ChainId">The chain it follows.</param>
/// <param name="DocumentType">The document type.</param>
/// <param name="DocumentId">The document's id in the app.</param>
/// <param name="Amount">The amount approvers are accountable for.</param>
/// <param name="Currency">Its currency, or null.</param>
/// <param name="OrgUnitId">The org unit owning the document, or null.</param>
/// <param name="RequestedBy">Who submitted it; they can never approve it.</param>
/// <param name="Summary">A short description for the approver's inbox.</param>
/// <param name="Steps">The steps, copied from the chain when it was submitted.</param>
/// <param name="CurrentStep">The step waiting for a decision (zero based); equals the step count once approved.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Decisions">Every decision so far.</param>
/// <param name="CreatedAt">When it was submitted.</param>
/// <param name="StepStartedAt">When the current step began waiting.</param>
/// <param name="Escalated">Whether the current step has been escalated.</param>
/// <param name="Version">Changes with every update; a decision is accepted only against the version it read.</param>
/// <param name="CompletedAt">When it was approved, rejected or cancelled.</param>
public sealed record ApprovalRequest(
    Guid Id,
    Guid? TenantId,
    Guid ChainId,
    string DocumentType,
    string DocumentId,
    decimal Amount,
    string? Currency,
    Guid? OrgUnitId,
    Guid RequestedBy,
    string? Summary,
    IReadOnlyList<ApprovalStep> Steps,
    int CurrentStep,
    ApprovalStatus Status,
    IReadOnlyList<ApprovalDecision> Decisions,
    DateTimeOffset CreatedAt,
    DateTimeOffset StepStartedAt,
    bool Escalated,
    long Version,
    DateTimeOffset? CompletedAt = null)
{
    /// <summary>The step waiting for a decision; null when the request is finished.</summary>
    public ApprovalStep? Step => Status == ApprovalStatus.Pending && CurrentStep < Steps.Count ? Steps[CurrentStep] : null;
}

/// <summary>What an app submits for approval. The amount must come from the stored document, never from the caller.</summary>
/// <param name="DocumentType">The document type, matched to a chain.</param>
/// <param name="DocumentId">The document id.</param>
/// <param name="Amount">The amount approvers are accountable for.</param>
/// <param name="Currency">Its currency, or null.</param>
/// <param name="OrgUnitId">The org unit owning the document, or null.</param>
/// <param name="Summary">A short description for the inbox.</param>
public sealed record ApprovalSubmission(string DocumentType, string DocumentId, decimal Amount, string? Currency = null, Guid? OrgUnitId = null, string? Summary = null);

/// <summary>The approval chains of the company.</summary>
public interface IApprovalChainStore
{
    /// <summary>Every chain.</summary>
    Task<IReadOnlyList<ApprovalChain>> ListAsync(CancellationToken ct = default);

    /// <summary>One chain; null when it does not exist.</summary>
    Task<ApprovalChain?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates the chain, or replaces the one with the same id.</summary>
    Task<ApprovalChain> SaveAsync(ApprovalChain chain, CancellationToken ct = default);

    /// <summary>Removes a chain (requests already under way keep their steps); false when it did not exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Where approval requests are kept.</summary>
public interface IApprovalRequestStore
{
    /// <summary>Stores a new request.</summary>
    Task AddAsync(ApprovalRequest request, CancellationToken ct = default);

    /// <summary>One request; null when it does not exist.</summary>
    Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Replaces the request when its stored version is <paramref name="expectedVersion"/>; false when someone else changed it first
    /// (so two approvers cannot both win). <paramref name="updated"/> must carry the next version.
    /// </summary>
    Task<bool> TryUpdateAsync(ApprovalRequest updated, long expectedVersion, CancellationToken ct = default);

    /// <summary>Requests that are still pending.</summary>
    Task<IReadOnlyList<ApprovalRequest>> PendingAsync(CancellationToken ct = default);

    /// <summary>Requests submitted by the user, newest first.</summary>
    Task<IReadOnlyList<ApprovalRequest>> RequestedByAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every request for a document, newest first.</summary>
    Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default);
}

/// <summary>Told what happens to approval requests (notify approvers, update the document, start the next job). Register any number.</summary>
public interface IApprovalWorkflowObserver
{
    /// <summary>Called after a change is saved. Must not throw; failures are logged and skipped.</summary>
    ValueTask OnChangedAsync(ApprovalRequest request, string change, CancellationToken ct = default);
}

/// <summary>The <c>change</c> values an <see cref="IApprovalWorkflowObserver"/> receives.</summary>
public static class ApprovalChanges
{
    /// <summary>A request was submitted.</summary>
    public const string Submitted = "submitted";

    /// <summary>A step was approved and the chain moved on.</summary>
    public const string StepApproved = "step-approved";

    /// <summary>One approver approved a step that needs more.</summary>
    public const string ApprovalRecorded = "approval-recorded";

    /// <summary>The last step was approved.</summary>
    public const string Approved = "approved";

    /// <summary>An approver refused the request.</summary>
    public const string Rejected = "rejected";

    /// <summary>The requester withdrew it.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>A step waited too long and was escalated.</summary>
    public const string Escalated = "escalated";
}

/// <summary>Settings of the approval workflow.</summary>
public sealed class ApprovalWorkflowOptions
{
    /// <summary>Whether the background service escalates overdue steps. Default <see langword="true"/>.</summary>
    public bool EnableEscalation { get; set; } = true;

    /// <summary>How often overdue steps are looked for.</summary>
    public TimeSpan EscalationCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The most steps a chain may have.</summary>
    public int MaxSteps { get; set; } = 20;
}
