using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Budgeting.Application.IntegrationEvents;
using ProcureFlow.Modules.Budgeting.Domain.Events;

namespace ProcureFlow.Modules.Budgeting.Application.DomainEventHandlers;

public sealed class BudgetCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<BudgetCreatedDomainEventHandler> logger) : IDomainEventHandler<BudgetCreatedDomainEvent>
{
    public Task HandleAsync(BudgetCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for budget created: {BudgetId}", @event.BudgetId);

        return moduleBus.PublishAsync(new BudgetCreatedIntegrationEvent(
            @event.BudgetId,
            @event.TenantId,
            @event.FiscalYear,
            @event.CostCenterId,
            @event.Category,
            @event.Amount,
            @event.OccurredAt), ct);
    }
}