using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Budgeting.Domain.Events;

[IntegrationEventName("Budgeting.BudgetCreated.v1")]
public sealed record BudgetCreatedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    Guid TenantId,
    int FiscalYear,
    Guid CostCenterId,
    string Category,
    decimal Amount,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Budgeting.BudgetCreated.v1";
}

public sealed record BudgetRevisionRequestedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    int Version,
    decimal NewAmount,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BudgetRevisionApprovedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    int Version,
    decimal Amount,
    string ApprovedBy,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BudgetReservedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    Guid ReferenceId,
    decimal Amount,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BudgetCommittedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    Guid ReferenceId,
    decimal Amount,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BudgetConsumedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    Guid ReferenceId,
    decimal Amount,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BudgetReleasedDomainEvent(
    Guid EventId,
    Guid BudgetId,
    Guid ReferenceId,
    decimal Amount,
    DateTime OccurredAt) : IDomainEvent;