using TradeFlow.Shared.Domain;

namespace TradeFlow.Shared.Application.Abstractions.Gateways;

/// <summary>
/// Command-side gateway into the Budgeting module (BRS §5.3). Implements the
/// reserve → commit → consume/release ledger lifecycle (BR-BUD-03) on behalf of
/// Procurement. Implemented by Budgeting.Infrastructure.
/// </summary>
public interface IBudgetGateway
{
    /// <summary>
    /// BR-PR-02: soft availability check performed when a requisition is
    /// submitted (does not mutate the ledger).
    /// </summary>
    Task<Result> CheckAvailabilityAsync(BudgetCheckRequest request, CancellationToken ct = default);

    /// <summary>
    /// BR-BUD-03: reserve funds against the budget (at PR approval). Creates an
    /// append-only ledger entry of type Reserve.
    /// </summary>
    Task<Result> ReserveAsync(BudgetLedgerRequest request, CancellationToken ct = default);

    /// <summary>
    /// BR-PO-05: commit funds (at PO approval) against an existing reservation.
    /// </summary>
    Task<Result> CommitAsync(BudgetLedgerRequest request, CancellationToken ct = default);

    /// <summary>
    /// BR-PR-06 / BR-PO-03: release a reservation or commitment when the
    /// reference document is cancelled or rejected.
    /// </summary>
    Task<Result> ReleaseAsync(BudgetReleaseRequest request, CancellationToken ct = default);
}

public sealed record BudgetCheckRequest(
    Guid TenantId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    decimal Amount,
    string Currency,
    Guid ReferenceId);

public sealed record BudgetLedgerRequest(
    Guid TenantId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    decimal Amount,
    string Currency,
    Guid ReferenceId,
    string ReferenceNumber,
    Guid PerformedBy);

public sealed record BudgetReleaseRequest(
    Guid TenantId,
    Guid ReferenceId,
    string Reason,
    Guid PerformedBy);
