namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// A document whose approval depends on its value (purchase order, payment, stock adjustment). The approval-authority
/// check compares <see cref="ApprovalAmount"/> with the limits configured for the approver.
/// </summary>
public interface IHasApprovalAmount
{
    /// <summary>The amount the approver is accountable for, in <see cref="ApprovalCurrency"/>.</summary>
    decimal ApprovalAmount { get; }

    /// <summary>The currency of <see cref="ApprovalAmount"/>; null when the document is in the company's own currency.</summary>
    string? ApprovalCurrency { get; }
}

/// <summary>
/// A document that remembers who has already acted on it (prepared, verified, approved, posted), so a rule can keep the
/// same person from holding two steps (separation of duties). Persist the ids on the server when each step happens.
/// </summary>
public interface IHasApprovalTrail
{
    /// <summary>The users who already performed a step on this document.</summary>
    IReadOnlyCollection<Guid> ActedByUserIds { get; }
}
