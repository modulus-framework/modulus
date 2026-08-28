using ProcureFlow.Modules.Customs.Domain.Entities;

namespace ProcureFlow.Modules.Customs.Application.Duty.Dtos;

public static class DutyResponseFactory
{
    public static DutyRateResponse ToResponse(DutyRate rate) => new(
        rate.Id, rate.HsCode, rate.Component, rate.Rate, rate.SpecificRate, rate.Uom,
        rate.EffectiveFrom, rate.EffectiveTo, rate.Source, rate.RefDoc, rate.Maker,
        rate.Checker, rate.Status);

    public static SroBenefitResponse ToResponse(SroBenefit benefit) => new(
        benefit.Id, benefit.Name, benefit.HsCodePrefix, benefit.Type,
        benefit.OverrideRate, benefit.CapPercent, benefit.Conditions,
        benefit.EffectiveFrom, benefit.EffectiveTo);

    public static BoeResponse ToResponse(BillOfEntry boe) => new(
        boe.Id, boe.TenantId, boe.FileId, boe.BoeNo, boe.BoeDate, boe.OfficeCode, boe.DeclarantAin,
        boe.Status, boe.Lane,
        boe.Lines.Sum(l => l.AssessedTtiBdt ?? 0m),
        boe.Challans.Sum(c => c.Amount),
        boe.Lines.Select(ToLineResponse).ToList(),
        boe.Challans.Select(c => new ChallanResponse(c.Id, c.ChallanNo, c.Amount, c.PaidAtUtc, c.EvidenceRef)).ToList(),
        boe.Disputes.Select(d => new DisputeResponse(d.Id, d.BoeLineId, d.VarianceAmount, d.TolerancePct,
            d.ResolutionType, d.GuaranteeRef, d.Status)).ToList(),
        boe.Milestones.Select(m => new MilestoneResponse(m.Stage, m.OccurredAtUtc)).ToList());

    public static BoeLineResponse ToLineResponse(BoeLine line) => new(
        line.Id, line.CiLineId, line.HsCode, line.Description, line.Quantity, line.Uom,
        line.DeclaredAvFcy, line.CustomsExchangeRate, line.LandingChargePct, line.TariffValueBdt,
        line.ComputedTtiBdt, line.AssessedTtiBdt,
        line.AssessedDutyLines.Select(d => new AssessedDutyLineResponse(d.Component, d.Amount)).ToList(),
        line.RateLineage.Select(r => new RateLineageResponse(r.Component, r.RateRowId, r.RateUsed)).ToList());

    public static ItemHsMappingResponse ToResponse(ItemHsMapping mapping) => new(
        mapping.Id, mapping.TenantId, mapping.ItemId, mapping.HsCode, mapping.Confidence,
        mapping.Status, mapping.Notes, mapping.MappedBy, mapping.MappedAtUtc,
        mapping.ApprovedBy, mapping.ApprovedAtUtc, mapping.RejectionReason,
        mapping.IsConsignmentOverride, mapping.OverrideFileId);
}