using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Customs.Application.IntegrationEvents;
using TradeFlow.Modules.Customs.Domain.Events;

namespace TradeFlow.Modules.Customs.Application.DomainEventHandlers;

public sealed class AitAtAdjustmentRecordedDomainEventHandler(
    IModuleBus moduleBus,
    ICurrentTenant currentTenant,
    ILogger<AitAtAdjustmentRecordedDomainEventHandler> logger) : IDomainEventHandler<AitAtAdjustmentRecordedDomainEvent>
{
    public Task HandleAsync(AitAtAdjustmentRecordedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "AIT/AT adjustment counterposted: company {CompanyId} FY {FiscalYear} {Component} {Amount:N2} (period {ReturnPeriod})",
            @event.CompanyId, @event.FiscalYear, @event.Component, @event.Amount, @event.ReturnPeriod);

        return moduleBus.PublishAsync(new AitAtAdjustmentRecordedIntegrationEvent(
            @event.EntryId,
            currentTenant.TenantId ?? Guid.Empty,
            @event.CompanyId,
            @event.FiscalYear,
            @event.Component.ToString(),
            @event.Amount,
            @event.ReturnPeriod,
            @event.BookedOn,
            @event.OccurredAt), ct);
    }
}