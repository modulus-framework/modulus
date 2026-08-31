using TradeFlow.Modules.Costing.Application.Dtos;
using TradeFlow.Modules.Costing.Domain.Entities;

namespace TradeFlow.Modules.Costing.Application;

public static class CostingResponseFactory
{
    public static LandedCostSheetResponse ToSheetResponse(LandedCostSheet sheet) => new(
        sheet.Id, sheet.TenantId, sheet.FileId, sheet.SheetNumber, sheet.Currency, sheet.Status,
        sheet.SheetVersion, sheet.FinalizedAtUtc,
        sheet.Lines.Select(l => new CostSheetLineResponse(l.Id, l.SourceLineId, l.GoodsValueFcy,
            l.GoodsValueBdt, l.ReceivedQty, l.TotalLandedCostBdt, l.UnitLandedCost,
            l.Allocations.Select(a => new LineAllocationResponse(a.ElementId, a.ElementName, a.AmountBdt,
                a.Treatment, a.IsResidual)).ToArray())).ToArray(),
        sheet.Elements.Select(e => new CostElementResponse(e.Id, e.Name, e.AmountFcy, e.FxRate, e.AmountBdt,
            e.Driver, e.Scope, e.Treatment, e.SourceDocType, e.SourceDocNumber)).ToArray());
}