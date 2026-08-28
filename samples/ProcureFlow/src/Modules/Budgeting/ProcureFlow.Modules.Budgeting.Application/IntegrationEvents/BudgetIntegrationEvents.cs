using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Budgeting.Application.IntegrationEvents;

public sealed record BudgetCreatedIntegrationEvent(
    Guid BudgetId,
    Guid TenantId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    decimal Amount,
    DateTime OccurredAtUtc) : IntegrationEventBase("Budgeting.BudgetCreated.v1")
{
    public Guid BudgetId { get; } = BudgetId;
    public Guid TenantId { get; } = TenantId;
    public int FiscalYear { get; } = FiscalYear;
    public Guid CostCenterId { get; } = CostCenterId;
    public string Category { get; } = Category;
    public decimal Amount { get; } = Amount;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}

public sealed record BudgetSoftExceededIntegrationEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid ReferenceId,
    decimal Amount,
    decimal Available,
    DateTime OccurredAtUtc) : IntegrationEventBase("Budgeting.BudgetSoftExceeded.v1")
{
    public Guid BudgetId { get; } = BudgetId;
    public Guid TenantId { get; } = TenantId;
    public Guid ReferenceId { get; } = ReferenceId;
    public decimal Amount { get; } = Amount;
    public decimal Available { get; } = Available;
    public DateTime OccurredAtUtc { get; } = OccurredAtUtc;
}