using ProcureFlow.Modules.Import.Application.Dtos;
using ProcureFlow.Modules.Import.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Application.Commands;

public sealed record CreateImportFileCommand(
    Guid CompanyId,
    int FiscalYear,
    Guid? PoId,
    string Incoterm,
    string Currency,
    string PortOfLoading,
    string PortOfDischarge,
    decimal EstimatedGoodsValue) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record LinkImportPoCommand(
    Guid FileId,
    Guid PoId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record AcceptPiCommand(
    Guid FileId,
    Guid PiId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record InstrumentFileCommand(
    Guid FileId,
    Guid? LcId,
    Guid? TtId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record AdvanceFileCommand(
    Guid FileId,
    ImportFileStatus ToStatus,
    Guid? ShipmentId = null,
    DateOnly? LandingDate = null,
    Guid? BoeId = null) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record AssignCnfAgentCommand(
    Guid FileId,
    Guid CnfAgentId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record HoldFileCommand(
    Guid FileId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record ResumeFileCommand(
    Guid FileId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record MarkDisputedCommand(
    Guid FileId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record CancelFileCommand(
    Guid FileId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record AddFileCostEntryCommand(
    Guid FileId,
    string Element,
    decimal AmountFcy,
    decimal AmountBdt,
    string Currency,
    string SourceDocType,
    Guid SourceDocId,
    string SourceDocNumber,
    CostDirection Direction) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record RegisterFileDocumentCommand(
    Guid FileId,
    string Type,
    string Name,
    bool IsMandatory,
    bool IsPresent) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record RegisterContainerCommand(
    Guid FileId,
    string ContainerNo,
    string SizeType,
    string IsoCode,
    string? SealNo) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record LandContainerCommand(
    Guid FileId,
    Guid ContainerId,
    DateOnly LandingDate) : Modulus.Mediator.Abstractions.ICommand<Result<ImportFileResponse>>;

public sealed record CreateProformaInvoiceCommand(
    Guid FileId,
    Guid PoId,
    string PiNumber,
    string Currency,
    string BeneficiaryName,
    string BeneficiaryBank,
    string BeneficiaryAccount,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    IReadOnlyList<ProFormaLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<ProformaInvoiceResponse>>;

public sealed record ProFormaLineInput(Guid? PoLineId, string Description, decimal Quantity, string Uom, decimal UnitPrice);

public sealed record ReconcilePiToPoCommand(
    Guid PiId,
    Guid PoLineId,
    decimal PoQuantity,
    decimal PoUnitPrice,
    decimal TolerancePct) : Modulus.Mediator.Abstractions.ICommand<Result<ProformaInvoiceResponse>>;

public sealed record AcceptPiForLcCommand(
    Guid PiId) : Modulus.Mediator.Abstractions.ICommand<Result<ProformaInvoiceResponse>>;

public sealed record CreateCommercialInvoiceCommand(
    Guid FileId,
    Guid? PiId,
    string CiNumber,
    string Currency,
    decimal TotalFcy,
    DateOnly IssuedOn,
    IReadOnlyList<CommercialLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<CommercialInvoiceResponse>>;

public sealed record CommercialLineInput(Guid? PiLineId, string Description, decimal Quantity, string Uom, decimal UnitPrice);

public sealed record ReconcileCiToPiCommand(
    Guid CiId,
    decimal PiTotal,
    decimal TolerancePct) : Modulus.Mediator.Abstractions.ICommand<Result<CommercialInvoiceResponse>>;

public sealed record CreateShipmentCommand(
    Guid FileId,
    Guid? CiId,
    string ShipmentNo,
    ShipmentMode Mode,
    string VesselVoyage,
    DateOnly Etd,
    DateOnly Eta) : Modulus.Mediator.Abstractions.ICommand<Result<ShipmentResponse>>;

public sealed record RecordEtaChangeCommand(
    Guid ShipmentId,
    DateOnly NewEta) : Modulus.Mediator.Abstractions.ICommand<Result<ShipmentResponse>>;

public sealed record CheckLcBreachRiskCommand(
    Guid ShipmentId,
    DateOnly LatestShipmentDate) : Modulus.Mediator.Abstractions.ICommand<Result<ShipmentResponse>>;

public sealed record CreateCnfAgentCommand(
    string Name,
    string AinNumber,
    string Contacts) : Modulus.Mediator.Abstractions.ICommand<Result<CnfAgentResponse>>;

public sealed record SetCnfRateCardCommand(
    Guid AgentId,
    decimal PerBoe,
    decimal PerContainer,
    decimal PctOfValue,
    decimal DocumentationCharges) : Modulus.Mediator.Abstractions.ICommand<Result<CnfAgentResponse>>;

// ── Packing List (BR-DOC-06) ────────────────────────────────────────

public sealed record CreatePackingListCommand(
    Guid FileId,
    Guid CiId,
    string PlNumber,
    int Cartons,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    decimal VolumeCbm,
    IReadOnlyList<PackingListLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<PackingListResponse>>;

public sealed record PackingListLineInput(
    Guid CiLineId,
    decimal Quantity,
    string Uom,
    decimal NetWeightKg,
    decimal GrossWeightKg,
    decimal VolumeCbm);

public sealed record ValidatePackingListCommand(
    Guid PlId,
    decimal CiQuantity,
    decimal TolerancePct) : Modulus.Mediator.Abstractions.ICommand<Result<PackingListResponse>>;

// ── Import Permit (BR-PM-01/02) ─────────────────────────────────────

public sealed record CreateImportPermitCommand(
    string PermitNo,
    string Category,
    decimal CeilingQty,
    decimal CeilingValue,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    string IssuedBy) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPermitResponse>>;

public sealed record DrawPermitCommand(
    Guid PermitId,
    Guid FileId,
    decimal Qty,
    decimal Value) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPermitResponse>>;

// ── Insurance (BR-INS-01) ───────────────────────────────────────────

public sealed record CreateInsurancePolicyCommand(
    Guid FileId,
    string PolicyNo,
    string Insurer,
    string CoverNoteRef,
    decimal InsuredValueFcy,
    decimal PremiumFcy,
    string Currency,
    DateOnly CoverStart) : Modulus.Mediator.Abstractions.ICommand<Result<InsurancePolicyResponse>>;

// ── Transport Document (BR-BL-01..03) ──────────────────────────────

public sealed record CreateTransportDocumentCommand(
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
    SurrenderStatus SurrenderStatus) : Modulus.Mediator.Abstractions.ICommand<Result<TransportDocumentResponse>>;

public sealed record TransferTransportDocumentCommand(
    Guid DocumentId,
    CustodyHolder NewHolder) : Modulus.Mediator.Abstractions.ICommand<Result<TransportDocumentResponse>>;

// ── Freight Cost (BR-FR-01/02) ─────────────────────────────────────

public sealed record CreateFreightCostCommand(
    Guid ShipmentId,
    Guid FileId,
    FreightCostType CostType,
    string Description,
    decimal Amount,
    string Currency,
    string? SurchargeType) : Modulus.Mediator.Abstractions.ICommand<Result<FreightCostResponse>>;

public sealed record CommitFreightCostToActualCommand(
    Guid FreightCostId,
    string InvoiceNo,
    DateOnly InvoiceDate) : Modulus.Mediator.Abstractions.ICommand<Result<FreightCostResponse>>;

// ── Bill of Entry (BR-CC-01..05) ───────────────────────────────────

public sealed record CreateBillOfEntryCommand(
    Guid FileId,
    string BoeNumber,
    DateOnly BoeDate,
    string CustomsOffice,
    Guid? CnfAgentId,
    BoeLane Lane,
    string DeclarantAin,
    IReadOnlyList<BoeLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record BoeLineInput(Guid? CiLineId, string HsCode, decimal AssessableValue, decimal Quantity, string Uom);

public sealed record SubmitBillOfEntryCommand(
    Guid BoeId) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record RecordBoeQueryCommand(
    Guid BoeId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record RecordBoeAssessmentCommand(
    Guid BoeId) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record RecordBoePaymentCommand(
    Guid BoeId) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record RecordBoeExaminationCommand(
    Guid BoeId,
    BoeLane Lane) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record ReleaseBillOfEntryCommand(
    Guid BoeId) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

public sealed record AddBoeDutyLineCommand(
    Guid BoeId,
    string Component,
    decimal Rate,
    decimal Amount,
    string? SroRef) : Modulus.Mediator.Abstractions.ICommand<Result<BillOfEntryResponse>>;

// ── Assessment Variance (BR-CC-03) ─────────────────────────────────

public sealed record CreateAssessmentVarianceCommand(
    Guid BoeId,
    Guid BoeLineId,
    VarianceType Type,
    string Component,
    decimal SystemAmount,
    decimal AssessedAmount,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<AssessmentVarianceResponse>>;

public sealed record ResolveAssessmentVarianceCommand(
    Guid VarianceId,
    string Resolution) : Modulus.Mediator.Abstractions.ICommand<Result<AssessmentVarianceResponse>>;

public sealed record AcceptAssessmentVarianceCommand(
    Guid VarianceId) : Modulus.Mediator.Abstractions.ICommand<Result<AssessmentVarianceResponse>>;

// ── Port Charges (BR-CC-04) ────────────────────────────────────────

public sealed record CreatePortChargeCommand(
    Guid FileId,
    PortChargeType ChargeType,
    decimal Amount,
    string Currency,
    DateOnly ChargedOn,
    string? Description) : Modulus.Mediator.Abstractions.ICommand<Result<PortChargeResponse>>;

// ── Import Planning (BR-IP-01..06) ─────────────────────────────────

public sealed record CreateImportPlanCommand(
    Guid CompanyId,
    int FiscalYear,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Currency,
    IReadOnlyList<ImportPlanLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record ImportPlanLineInput(
    Guid? ItemId,
    Guid? CategoryId,
    string Description,
    decimal EstQty,
    decimal EstFob,
    decimal EstLanded,
    decimal? TargetMonth,
    string? SourceCountry);

public sealed record AddImportPlanLineCommand(
    Guid PlanId,
    Guid? ItemId,
    Guid? CategoryId,
    string Description,
    decimal EstQty,
    decimal EstFob,
    decimal EstLanded,
    decimal? TargetMonth,
    string? SourceCountry) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record RemoveImportPlanLineCommand(
    Guid PlanId,
    Guid LineId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record SubmitImportPlanCommand(
    Guid PlanId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record ApproveImportPlanCommand(
    Guid PlanId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record ReviseImportPlanCommand(
    Guid PlanId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record CloseImportPlanCommand(
    Guid PlanId) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

public sealed record RecordPlanActualsCommand(
    Guid PlanId,
    Guid LineId,
    decimal Qty,
    decimal Fob,
    decimal Landed) : Modulus.Mediator.Abstractions.ICommand<Result<ImportPlanResponse>>;

// ── Certificate of Origin (BR-COO-01..06) ──────────────────────────

public sealed record CreateCertificateOfOriginCommand(
    Guid FileId,
    Guid? CiId,
    CertificateOfOriginType Type,
    string OriginCountry,
    string DocumentNo,
    string? IssuerName,
    DateOnly IssuedOn,
    DateOnly? ExpiryDate) : Modulus.Mediator.Abstractions.ICommand<Result<CertificateOfOriginResponse>>;

public sealed record CheckCooOriginMismatchCommand(
    Guid FileId,
    string CiOriginCountry) : Modulus.Mediator.Abstractions.ICommand<Result<CertificateOfOriginResponse>>;

public sealed record CreateCooIssuerRegistryCommand(
    string Country,
    string IssuerName,
    string? LicenseNo,
    DateOnly ValidFrom,
    DateOnly? ValidTo) : Modulus.Mediator.Abstractions.ICommand<Result<CooIssuerRegistryResponse>>;