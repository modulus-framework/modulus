using TradeFlow.Modules.Import.Domain.Entities;

namespace TradeFlow.Modules.Import.Application.Dtos;

public sealed record ImportMilestoneResponse(Guid Id, string Name, string Note, DateTime OccurredAtUtc);

public sealed record ImportContainerResponse(
    Guid Id,
    string ContainerNo,
    string SizeType,
    string IsoCode,
    string? SealNo,
    DateOnly? FreeDaysEnd,
    int DemurrageDays);

public sealed record ImportCostEntryResponse(
    Guid Id,
    string Element,
    decimal AmountFcy,
    decimal AmountBdt,
    string Currency,
    string SourceDocType,
    string SourceDocNumber,
    CostDirection Direction);

public sealed record FileDocumentResponse(Guid Id, string Type, string Name, bool IsMandatory, bool IsPresent);

public sealed record ImportFileResponse(
    Guid Id,
    Guid TenantId,
    string FileNumber,
    Guid CompanyId,
    int FiscalYear,
    int Sequence,
    Guid? PoId,
    Guid? PiId,
    Guid? LcId,
    Guid? TtId,
    Guid? BoeId,
    Guid? CnfAgentId,
    string Incoterm,
    string Currency,
    string PortOfLoading,
    string PortOfDischarge,
    decimal EstimatedGoodsValue,
    ImportFileStatus Status,
    DateOnly? LandingDate,
    decimal ClearingBalance,
    bool HasUnmatchedImpForm,
    bool HasMissingMandatoryDocuments,
    string? HoldReason,
    string? DisputeReason,
    string? CancellationReason,
    IReadOnlyList<ImportMilestoneResponse> Milestones,
    IReadOnlyList<ImportContainerResponse> Containers,
    IReadOnlyList<ImportCostEntryResponse> CostEntries,
    IReadOnlyList<FileDocumentResponse> Documents);

public sealed record ProformaInvoiceLineResponse(Guid Id, Guid? PoLineId, string Description, decimal Quantity, string Uom, decimal UnitPrice);

public sealed record ProformaInvoiceResponse(
    Guid Id,
    Guid FileId,
    string PiNumber,
    string Currency,
    string BeneficiaryName,
    string BeneficiaryBank,
    string BeneficiaryAccount,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    decimal TotalFcy,
    DocumentReconciliationStatus Status,
    bool AcceptedForLc,
    IReadOnlyList<ProformaInvoiceLineResponse> Lines);

public sealed record CommercialInvoiceResponse(
    Guid Id,
    Guid FileId,
    Guid? PiId,
    string CiNumber,
    string Currency,
    decimal TotalFcy,
    DateOnly IssuedOn,
    DocumentReconciliationStatus Status,
    IReadOnlyList<CommercialInvoiceLineResponse> Lines);

public sealed record CommercialInvoiceLineResponse(Guid Id, Guid? PiLineId, string Description, decimal Quantity, string Uom, decimal UnitPrice);

public sealed record ShipmentResponse(
    Guid Id,
    Guid FileId,
    Guid? CiId,
    string ShipmentNo,
    ShipmentMode Mode,
    string VesselVoyage,
    DateOnly Etd,
    DateOnly Eta,
    DateOnly? ActualEta,
    int EtaSlippageDays,
    bool LcBreachRiskAlerted);

public sealed record CnfAgentResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string AinNumber,
    string Contacts,
    decimal RateCardPerBoe,
    decimal RateCardPerContainer,
    decimal RateCardPctOfValue,
    decimal RateCardDocumentationCharges);

// ── Packing List (BR-DOC-06) ────────────────────────────────────────

public sealed record PackingListLineResponse(
    Guid Id,
    Guid CiLineId,
    decimal Quantity,
    string Uom,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    decimal VolumeCbm);

public sealed record PackingListResponse(
    Guid Id,
    Guid FileId,
    Guid CiId,
    string PlNumber,
    int Cartons,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    decimal VolumeCbm,
    IReadOnlyList<PackingListLineResponse> Lines);

// ── Import Permit (BR-PM-01/02) ─────────────────────────────────────

public sealed record PermitUtilizationResponse(
    Guid Id,
    Guid FileId,
    DateOnly DrawnOn,
    decimal QtyDrawn,
    decimal ValueDrawn);

public sealed record ImportPermitResponse(
    Guid Id,
    string PermitNo,
    string Category,
    decimal CeilingQty,
    decimal CeilingValue,
    decimal UsedQty,
    decimal UsedValue,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    string IssuedBy,
    IReadOnlyList<PermitUtilizationResponse> Utilizations);

// ── Insurance (BR-INS-01) ───────────────────────────────────────────

public sealed record InsurancePolicyResponse(
    Guid Id,
    Guid FileId,
    string PolicyNo,
    string Insurer,
    string CoverNoteRef,
    decimal InsuredValueFcy,
    decimal PremiumFcy,
    string Currency,
    DateOnly CoverStart);

// ── Transport Document (BR-BL-01..03) ──────────────────────────────

public sealed record TransportDocumentResponse(
    Guid Id,
    Guid ShipmentId,
    Guid FileId,
    TransportDocumentType Type,
    string DocumentNumber,
    DateOnly IssueDate,
    DateOnly? OnBoardDate,
    string FreightTerms,
    string Consignee,
    string NotifyParty,
    int OriginalCount,
    SurrenderStatus SurrenderStatus,
    CustodyHolder CustodyHolder,
    DateOnly? EndorsedAt);

// ── Freight Cost (BR-FR-01/02) ─────────────────────────────────────

public sealed record FreightCostResponse(
    Guid Id,
    Guid ShipmentId,
    Guid FileId,
    FreightCostType CostType,
    FreightStage Stage,
    string Description,
    decimal Amount,
    string Currency,
    string? SurchargeType,
    string? InvoiceNo,
    DateOnly? InvoiceDate);

// ── Bill of Entry (BR-CC-01..05) ───────────────────────────────────

public sealed record BoeLineDto(
    Guid Id,
    Guid? CiLineId,
    string HsCode,
    decimal AssessableValue,
    decimal Quantity,
    string Uom);

public sealed record BoeDutyLineDto(
    Guid Id,
    string Component,
    decimal Rate,
    decimal Amount,
    string? SroRef);

public sealed record BoeMilestoneDto(
    Guid Id,
    string Name,
    DateTime AtUtc);

public sealed record BillOfEntryResponse(
    Guid Id,
    Guid FileId,
    string BoeNumber,
    DateOnly BoeDate,
    string CustomsOffice,
    Guid? CnfAgentId,
    BoeLane Lane,
    string DeclarantAin,
    BoeStatus Status,
    decimal TotalAssessableValue,
    decimal TotalDuty,
    DateOnly? AssessedAt,
    DateOnly? PaidAt,
    DateOnly? ReleasedAt,
    string? DisputeReason,
    IReadOnlyList<BoeLineDto> Lines,
    IReadOnlyList<BoeDutyLineDto> DutyLines,
    IReadOnlyList<BoeMilestoneDto> Milestones);

// ── Assessment Variance (BR-CC-03) ─────────────────────────────────

public sealed record AssessmentVarianceResponse(
    Guid Id,
    Guid BoeId,
    Guid BoeLineId,
    VarianceType Type,
    string Component,
    decimal SystemAmount,
    decimal AssessedAmount,
    decimal VarianceAmount,
    string Reason,
    VarianceStatus Status,
    string? Resolution);

// ── Port Charges (BR-CC-04) ────────────────────────────────────────

public sealed record PortChargeResponse(
    Guid Id,
    Guid FileId,
    PortChargeType ChargeType,
    decimal Amount,
    string Currency,
    string? ReceiptRef,
    DateOnly ChargedOn,
    string? Description);

// ── Import Planning (BR-IP-01..06) ─────────────────────────────────

public sealed record ImportPlanLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? CategoryId,
    string Description,
    decimal EstQty,
    decimal EstFob,
    decimal EstLanded,
    decimal? TargetMonth,
    string? SourceCountry,
    decimal ActualQty,
    decimal ActualFob,
    decimal ActualLanded);

public sealed record ImportPlanResponse(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    int FiscalYear,
    string PlanNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Currency,
    ImportPlanStatus Status,
    int PlanVersion,
    decimal TotalEstFob,
    decimal TotalEstLanded,
    Guid? ApprovedBy,
    DateTime? ApprovedAtUtc,
    IReadOnlyList<ImportPlanLineResponse> Lines);

// ── Certificate of Origin (BR-COO-01..06) ──────────────────────────

public sealed record CertificateOfOriginResponse(
    Guid Id,
    Guid TenantId,
    Guid FileId,
    Guid? CiId,
    CertificateOfOriginType Type,
    string OriginCountry,
    string DocumentNo,
    string? IssuerName,
    DateOnly IssuedOn,
    DateOnly? ExpiryDate,
    bool PreferentialEligible,
    bool HasOriginMismatch,
    string? MismatchReason);

public sealed record CooIssuerRegistryResponse(
    Guid Id,
    Guid TenantId,
    string Country,
    string IssuerName,
    string? LicenseNo,
    DateOnly ValidFrom,
    DateOnly? ValidTo);