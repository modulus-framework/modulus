using ProcureFlow.Modules.Import.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Application.Queries;

public sealed record GetImportFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<ImportFileResponse>>;

public sealed record ListImportFilesQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ImportFileResponse>>>;

public sealed record GetProformaInvoiceQuery(Guid PiId) : Modulus.Mediator.Abstractions.IQuery<Result<ProformaInvoiceResponse>>;

public sealed record GetCommercialInvoiceQuery(Guid CiId) : Modulus.Mediator.Abstractions.IQuery<Result<CommercialInvoiceResponse>>;

public sealed record ListProformaInvoicesQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ProformaInvoiceResponse>>>;

public sealed record GetShipmentQuery(Guid ShipmentId) : Modulus.Mediator.Abstractions.IQuery<Result<ShipmentResponse>>;

public sealed record GetTransportDocumentQuery(Guid DocumentId) : Modulus.Mediator.Abstractions.IQuery<Result<TransportDocumentResponse>>;

public sealed record ListTransportDocumentsByShipmentQuery(Guid ShipmentId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<TransportDocumentResponse>>>;

public sealed record GetFreightCostQuery(Guid FreightCostId) : Modulus.Mediator.Abstractions.IQuery<Result<FreightCostResponse>>;

public sealed record ListFreightCostsByShipmentQuery(Guid ShipmentId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<FreightCostResponse>>>;

public sealed record ListFreightCostsByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<FreightCostResponse>>>;

public sealed record GetBillOfEntryQuery(Guid BoeId) : Modulus.Mediator.Abstractions.IQuery<Result<BillOfEntryResponse>>;

public sealed record GetBillOfEntryByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<BillOfEntryResponse>>;

public sealed record ListAssessmentVariancesByBoeQuery(Guid BoeId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<AssessmentVarianceResponse>>>;

public sealed record ListPortChargesByFileQuery(Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<PortChargeResponse>>>;

// ── Import Planning (BR-IP-01..06) ─────────────────────────────────

public sealed record GetImportPlanQuery(Guid PlanId) : Modulus.Mediator.Abstractions.IQuery<Result<ImportPlanResponse>>;

public sealed record ListImportPlansQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ImportPlanResponse>>>;

public sealed record ListImportPlansByFiscalYearQuery(int FiscalYear) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ImportPlanResponse>>>;

public sealed record GetPlanAdherenceReportQuery(Guid PlanId) : Modulus.Mediator.Abstractions.IQuery<Result<PlanAdherenceReportResponse>>;

public sealed record PlanAdherenceReportResponse(
    Guid PlanId,
    string PlanNumber,
    int FiscalYear,
    decimal TotalEstFob,
    decimal TotalEstLanded,
    decimal ActualFob,
    decimal ActualLanded,
    decimal FobVariancePct,
    decimal LandedVariancePct,
    IReadOnlyList<PlanLineAdherenceResponse> Lines);

public sealed record PlanLineAdherenceResponse(
    Guid LineId,
    string Description,
    decimal EstQty,
    decimal ActualQty,
    decimal EstFob,
    decimal ActualFob,
    decimal QtyVariancePct,
    decimal FobVariancePct);
public sealed record CreateCertificateOfOriginQuery(
    Guid FileId,
    Guid? CiId,
    CertificateOfOriginType Type,
    string OriginCountry,
    string DocumentNo,
    string? IssuerName,
    DateOnly IssuedOn,
    DateOnly? ExpiryDate) : Modulus.Mediator.Abstractions.IQuery<Result<CertificateOfOriginResponse>>;

public sealed record CheckCooOriginMismatchQuery(
    Guid FileId,
    string CiOriginCountry) : Modulus.Mediator.Abstractions.IQuery<Result<CertificateOfOriginResponse>>;

public sealed record CreateCooIssuerRegistryQuery(
    Guid TenantId,
    string Country,
    string IssuerName,
    string? LicenseNo,
    DateOnly ValidFrom,
    DateOnly? ValidTo) : Modulus.Mediator.Abstractions.IQuery<Result<CooIssuerRegistryResponse>>;
