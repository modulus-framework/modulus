using ProcureFlow.Modules.Import.Application.Dtos;
using ProcureFlow.Modules.Import.Domain.Entities;

namespace ProcureFlow.Modules.Import.Application;

public static class ImportResponseFactory
{
    public static ImportFileResponse ToFileResponse(ImportFile file) => new(
        file.Id,
        file.TenantId,
        file.FileNumber,
        file.CompanyId,
        file.FiscalYear,
        file.Sequence,
        file.PoId,
        file.PiId,
        file.LcId,
        file.TtId,
        file.BoeId,
        file.CnfAgentId,
        file.Incoterm,
        file.Currency,
        file.PortOfLoading,
        file.PortOfDischarge,
        file.EstimatedGoodsValue,
        file.Status,
        file.LandingDate,
        file.ClearingBalance,
        file.HasUnmatchedImpForm,
        file.HasMissingMandatoryDocuments,
        file.HoldReason,
        file.DisputeReason,
        file.CancellationReason,
        file.Milestones.Select(m => new ImportMilestoneResponse(m.Id, m.Name, m.Note, m.OccurredAtUtc)).ToArray(),
        file.Containers.Select(c => new ImportContainerResponse(c.Id, c.ContainerNo, c.SizeType, c.IsoCode,
            c.SealNo, c.FreeDaysEnd, c.FreeDaysEnd is null ? 0 : c.DemurrageDays(DateOnly.FromDateTime(DateTime.UtcNow)))).ToArray(),
        file.CostEntries.Select(e => new ImportCostEntryResponse(e.Id, e.Element, e.AmountFcy, e.AmountBdt,
            e.Currency, e.SourceDocType, e.SourceDocNumber, e.Direction)).ToArray(),
        file.Documents.Select(d => new FileDocumentResponse(d.Id, d.Type, d.Name, d.IsMandatory, d.IsPresent)).ToArray());

    public static ProformaInvoiceResponse ToPiResponse(ProformaInvoice pi) => new(
        pi.Id,
        pi.FileId,
        pi.PiNumber,
        pi.Currency,
        pi.BeneficiaryName,
        pi.BeneficiaryBank,
        pi.BeneficiaryAccount,
        pi.IssuedOn,
        pi.ValidUntil,
        pi.TotalFcy,
        pi.Status,
        pi.AcceptedForLc,
        pi.Lines.Select(l => new ProformaInvoiceLineResponse(l.Id, l.PoLineId, l.Description, l.Quantity, l.Uom, l.UnitPrice)).ToArray());

    public static CommercialInvoiceResponse ToCiResponse(CommercialInvoice ci) => new(
        ci.Id,
        ci.FileId,
        ci.PiId,
        ci.CiNumber,
        ci.Currency,
        ci.TotalFcy,
        ci.IssuedOn,
        ci.Status,
        ci.Lines.Select(l => new CommercialInvoiceLineResponse(l.Id, l.PiLineId, l.Description, l.Quantity, l.Uom, l.UnitPrice)).ToArray());

    public static ShipmentResponse ToShipmentResponse(Shipment shipment) => new(
        shipment.Id,
        shipment.FileId,
        shipment.CiId,
        shipment.ShipmentNo,
        shipment.Mode,
        shipment.VesselVoyage,
        shipment.Etd,
        shipment.Eta,
        shipment.ActualEta,
        shipment.EtaSlippageDays,
        shipment.LcBreachRiskAlerted);

    public static CnfAgentResponse ToAgentResponse(CnfAgent agent) => new(
        agent.Id,
        agent.TenantId,
        agent.Name,
        agent.AinNumber,
        agent.Contacts,
        agent.RateCardPerBoe,
        agent.RateCardPerContainer,
        agent.RateCardPctOfValue,
        agent.RateCardDocumentationCharges);

    public static PackingListResponse ToPackingListResponse(PackingList pl) => new(
        pl.Id,
        pl.FileId,
        pl.CiId,
        pl.PlNumber,
        pl.Cartons,
        pl.NetWeightKg,
        pl.GrossWeightKg,
        pl.VolumeCbm,
        pl.Lines.Select(l => new PackingListLineResponse(l.Id, l.CiLineId, l.Quantity, l.Uom,
            l.NetWeightKg, l.GrossWeightKg, l.VolumeCbm)).ToArray());

    public static ImportPermitResponse ToPermitResponse(ImportPermit permit) => new(
        permit.Id,
        permit.PermitNo,
        permit.Category,
        permit.CeilingQty,
        permit.CeilingValue,
        permit.DrawnQty,
        permit.DrawnValue,
        permit.IssuedOn,
        permit.ExpiresOn,
        permit.IssuedBy,
        permit.Utilizations.Select(u => new PermitUtilizationResponse(u.Id, u.FileId, u.DrawnOn,
            u.Quantity, u.Value)).ToArray());

    public static InsurancePolicyResponse ToInsuranceResponse(InsurancePolicy policy) => new(
        policy.Id,
        policy.FileId,
        policy.PolicyNo,
        policy.Insurer,
        policy.CoverNoteRef,
        policy.InsuredValueFcy,
        policy.PremiumFcy,
        policy.Currency,
        policy.CoverStart);

    public static TransportDocumentResponse ToTransportDocumentResponse(TransportDocument document) => new(
        document.Id,
        document.ShipmentId,
        document.FileId,
        document.Type,
        document.DocumentNumber,
        document.IssueDate,
        document.OnBoardDate,
        document.FreightTerms,
        document.Consignee,
        document.NotifyParty,
        document.OriginalCount,
        document.SurrenderStatus,
        document.CustodyHolder,
        document.EndorsedAt);

    public static FreightCostResponse ToFreightCostResponse(FreightCost cost) => new(
        cost.Id,
        cost.ShipmentId,
        cost.FileId,
        cost.CostType,
        cost.Stage,
        cost.Description,
        cost.Amount,
        cost.Currency,
        cost.SurchargeType,
        cost.InvoiceNo,
        cost.InvoiceDate);

    public static BillOfEntryResponse ToBoeResponse(BillOfEntry boe) => new(
        boe.Id,
        boe.FileId,
        boe.BoeNumber,
        boe.BoeDate,
        boe.CustomsOffice,
        boe.CnfAgentId,
        boe.Lane,
        boe.DeclarantAin,
        boe.Status,
        boe.TotalAssessableValue,
        boe.TotalDuty,
        boe.AssessedAt,
        boe.PaidAt,
        boe.ReleasedAt,
        boe.DisputeReason,
        boe.Lines.Select(l => new BoeLineDto(l.Id, l.CiLineId, l.HsCode, l.AssessableValue, l.Quantity, l.Uom)).ToArray(),
        boe.DutyLines.Select(d => new BoeDutyLineDto(d.Id, d.Component, d.Rate, d.Amount, d.SroRef)).ToArray(),
        boe.Milestones.Select(m => new BoeMilestoneDto(m.Id, m.Name, m.AtUtc)).ToArray());

    public static AssessmentVarianceResponse ToVarianceResponse(AssessmentVariance variance) => new(
        variance.Id,
        variance.BoeId,
        variance.BoeLineId,
        variance.Type,
        variance.Component,
        variance.SystemAmount,
        variance.AssessedAmount,
        variance.VarianceAmount,
        variance.Reason,
        variance.Status,
        variance.Resolution);

    public static PortChargeResponse ToPortChargeResponse(PortCharge charge) => new(
        charge.Id,
        charge.FileId,
        charge.ChargeType,
        charge.Amount,
        charge.Currency,
        charge.ReceiptRef,
        charge.ChargedOn,
        charge.Description);

    // ── Import Planning (BR-IP-01..06) ─────────────────────────────

    public static ImportPlanResponse ToPlanResponse(ImportPlan plan) => new(
        plan.Id,
        plan.TenantId,
        plan.CompanyId,
        plan.FiscalYear,
        plan.PlanNumber,
        plan.PeriodStart,
        plan.PeriodEnd,
        plan.Currency,
        plan.Status,
        plan.PlanVersion,
        plan.TotalEstFob,
        plan.TotalEstLanded,
        plan.ApprovedBy,
        plan.ApprovedAtUtc,
        plan.Lines.Select(ToPlanLineResponse).ToArray());

    public static ImportPlanLineResponse ToPlanLineResponse(ImportPlanLine line) => new(
        line.Id,
        line.ItemId,
        line.CategoryId,
        line.Description,
        line.EstQty,
        line.EstFob,
        line.EstLanded,
        line.TargetMonth,
        line.SourceCountry,
        line.ActualQty,
        line.ActualFob,
        line.ActualLanded);
}