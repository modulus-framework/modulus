using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Costing.Application;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Modules.Costing.Domain.Services;
using TradeFlow.Modules.Customs.Application.IntegrationEvents;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.IntegrationEventHandlers;

/// <summary>
/// Handles BoE assessed events from the Customs module by creating cost elements
/// for duty components and adding them to the cost sheet for the import file.
/// Implements the duty cascade -> cost element wiring (BRS §6.1, §6.6).
/// </summary>
public sealed class BoeAssessedIntegrationEventHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ILogger<BoeAssessedIntegrationEventHandler> logger) : IIntegrationEventHandler<BoeAssessedIntegrationEvent>
{
    public async Task HandleAsync(BoeAssessedIntegrationEvent @event, CancellationToken ct)
    {
        if (@event.FileId is null)
        {
            logger.LogWarning("BoE {BoeNo} is not linked to an import file; skipping cost element creation", @event.BoeNo);
            return;
        }

        Guid tenantId = currentTenant.TenantId ?? @event.TenantId;

        LandedCostSheet? sheet = await repository.GetByFileAsync(tenantId, @event.FileId.Value, ct);
        if (sheet is null)
        {
            logger.LogInformation("No cost sheet found for file {FileId}; auto-creating sheet for BoE {BoeNo}",
                @event.FileId, @event.BoeNo);

            string sheetNumber = $"LCS-{@event.FileId.Value:N}-001";
            sheet = LandedCostSheet.Create(tenantId, @event.FileId.Value, sheetNumber, "BDT");
            await repository.AddAsync(sheet, ct);
        }

        if (sheet.Status is CostSheetStatus.Finalized or CostSheetStatus.Adjusted)
        {
            logger.LogWarning("Cost sheet {SheetNumber} for file {FileId} is already finalized; not adding duty elements",
                sheet.SheetNumber, @event.FileId);
            return;
        }

        var dutyComponents = @event.AssessedDutyLines
            .Select(d => new DutyComponentData(d.Component, d.Amount))
            .ToList();

        IReadOnlyList<CostElement> dutyElements = DutyCostElementMapper.MapFromBoeAssessment(
            tenantId,
            @event.FileId.Value,
            @event.BoeNo,
            dutyComponents,
            @event.CustomsExchangeRate);

        if (dutyElements.Count == 0)
        {
            logger.LogInformation("No duty elements to add from BoE {BoeNo} (empty assessment)", @event.BoeNo);
            return;
        }

        foreach (CostElement element in dutyElements)
        {
            Result result = sheet.AddElement(element);
            if (result.IsFailure)
            {
                logger.LogError("Failed to add cost element {ElementName}: {Error}", element.Name, result.Error.Message);
            }
        }

        await repository.SaveAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation(
            "Added {Count} duty cost elements to sheet {SheetNumber} from BoE {BoeNo} (TTI: {Tti:N2} BDT)",
            dutyElements.Count, sheet.SheetNumber, @event.BoeNo, @event.AssessedTti);
    }
}