using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Import.Application.Dtos;
using TradeFlow.Modules.Import.Application.Queries;
using TradeFlow.Modules.Import.Domain.Entities;
using TradeFlow.Modules.Import.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.Application.Queries;

public sealed class GetImportFileHandler(IImportFileRepository repository) : IQueryHandler<GetImportFileQuery, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(GetImportFileQuery query, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(query.FileId, ct);
        return file is null
            ? Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"))
            : Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class ListImportFilesHandler(
    IImportFileRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListImportFilesQuery, Result<IReadOnlyList<ImportFileResponse>>>
{
    public async Task<Result<IReadOnlyList<ImportFileResponse>>> HandleAsync(ListImportFilesQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<ImportFile> files = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<ImportFileResponse>>(files.Select(ImportResponseFactory.ToFileResponse).ToArray());
    }
}

public sealed class GetProformaInvoiceHandler(IProformaInvoiceRepository repository) : IQueryHandler<GetProformaInvoiceQuery, Result<ProformaInvoiceResponse>>
{
    public async Task<Result<ProformaInvoiceResponse>> HandleAsync(GetProformaInvoiceQuery query, CancellationToken ct)
    {
        ProformaInvoice? pi = await repository.GetByIdAsync(query.PiId, ct);
        return pi is null
            ? Result.Failure<ProformaInvoiceResponse>(Error.NotFound("Pi.NotFound", "PI not found"))
            : Result.Success(ImportResponseFactory.ToPiResponse(pi));
    }
}

public sealed class ListProformaInvoicesHandler(IProformaInvoiceRepository repository) : IQueryHandler<ListProformaInvoicesQuery, Result<IReadOnlyList<ProformaInvoiceResponse>>>
{
    public async Task<Result<IReadOnlyList<ProformaInvoiceResponse>>> HandleAsync(ListProformaInvoicesQuery query, CancellationToken ct)
    {
        IReadOnlyList<ProformaInvoice> pis = await repository.GetByFileAsync(query.FileId, ct);
        return Result.Success<IReadOnlyList<ProformaInvoiceResponse>>(pis.Select(ImportResponseFactory.ToPiResponse).ToArray());
    }
}

public sealed class GetCommercialInvoiceHandler(ICommercialInvoiceRepository repository) : IQueryHandler<GetCommercialInvoiceQuery, Result<CommercialInvoiceResponse>>
{
    public async Task<Result<CommercialInvoiceResponse>> HandleAsync(GetCommercialInvoiceQuery query, CancellationToken ct)
    {
        CommercialInvoice? ci = await repository.GetByIdAsync(query.CiId, ct);
        return ci is null
            ? Result.Failure<CommercialInvoiceResponse>(Error.NotFound("Ci.NotFound", "CI not found"))
            : Result.Success(ImportResponseFactory.ToCiResponse(ci));
    }
}

public sealed class GetShipmentHandler(IShipmentRepository repository) : IQueryHandler<GetShipmentQuery, Result<ShipmentResponse>>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(GetShipmentQuery query, CancellationToken ct)
    {
        Shipment? shipment = await repository.GetByIdAsync(query.ShipmentId, ct);
        return shipment is null
            ? Result.Failure<ShipmentResponse>(Error.NotFound("Shipment.NotFound", "Shipment not found"))
            : Result.Success(ImportResponseFactory.ToShipmentResponse(shipment));
    }
}

public sealed class GetTransportDocumentHandler(ITransportDocumentRepository repository) : IQueryHandler<GetTransportDocumentQuery, Result<TransportDocumentResponse>>
{
    public async Task<Result<TransportDocumentResponse>> HandleAsync(GetTransportDocumentQuery query, CancellationToken ct)
    {
        TransportDocument? document = await repository.GetByIdAsync(query.DocumentId, ct);
        return document is null
            ? Result.Failure<TransportDocumentResponse>(Error.NotFound("TransportDoc.NotFound", "Transport document not found"))
            : Result.Success(ImportResponseFactory.ToTransportDocumentResponse(document));
    }
}

public sealed class ListTransportDocumentsByShipmentHandler(ITransportDocumentRepository repository) : IQueryHandler<ListTransportDocumentsByShipmentQuery, Result<IReadOnlyList<TransportDocumentResponse>>>
{
    public async Task<Result<IReadOnlyList<TransportDocumentResponse>>> HandleAsync(ListTransportDocumentsByShipmentQuery query, CancellationToken ct)
    {
        IReadOnlyList<TransportDocument> documents = await repository.GetByShipmentAsync(query.ShipmentId, ct);
        return Result.Success<IReadOnlyList<TransportDocumentResponse>>(documents.Select(ImportResponseFactory.ToTransportDocumentResponse).ToArray());
    }
}

public sealed class GetFreightCostHandler(IFreightCostRepository repository) : IQueryHandler<GetFreightCostQuery, Result<FreightCostResponse>>
{
    public async Task<Result<FreightCostResponse>> HandleAsync(GetFreightCostQuery query, CancellationToken ct)
    {
        FreightCost? cost = await repository.GetByIdAsync(query.FreightCostId, ct);
        return cost is null
            ? Result.Failure<FreightCostResponse>(Error.NotFound("FreightCost.NotFound", "Freight cost not found"))
            : Result.Success(ImportResponseFactory.ToFreightCostResponse(cost));
    }
}

public sealed class ListFreightCostsByShipmentHandler(IFreightCostRepository repository) : IQueryHandler<ListFreightCostsByShipmentQuery, Result<IReadOnlyList<FreightCostResponse>>>
{
    public async Task<Result<IReadOnlyList<FreightCostResponse>>> HandleAsync(ListFreightCostsByShipmentQuery query, CancellationToken ct)
    {
        IReadOnlyList<FreightCost> costs = await repository.GetByShipmentAsync(query.ShipmentId, ct);
        return Result.Success<IReadOnlyList<FreightCostResponse>>(costs.Select(ImportResponseFactory.ToFreightCostResponse).ToArray());
    }
}

public sealed class ListFreightCostsByFileHandler(IFreightCostRepository repository) : IQueryHandler<ListFreightCostsByFileQuery, Result<IReadOnlyList<FreightCostResponse>>>
{
    public async Task<Result<IReadOnlyList<FreightCostResponse>>> HandleAsync(ListFreightCostsByFileQuery query, CancellationToken ct)
    {
        IReadOnlyList<FreightCost> costs = await repository.GetByFileAsync(query.FileId, ct);
        return Result.Success<IReadOnlyList<FreightCostResponse>>(costs.Select(ImportResponseFactory.ToFreightCostResponse).ToArray());
    }
}

public sealed class GetBillOfEntryHandler(IBillOfEntryRepository repository) : IQueryHandler<GetBillOfEntryQuery, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(GetBillOfEntryQuery query, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(query.BoeId, ct);
        return boe is null
            ? Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"))
            : Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class GetBillOfEntryByFileHandler(IBillOfEntryRepository repository) : IQueryHandler<GetBillOfEntryByFileQuery, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(GetBillOfEntryByFileQuery query, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByFileAsync(query.FileId, ct);
        return boe is null
            ? Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found for this file"))
            : Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class ListAssessmentVariancesByBoeHandler(IAssessmentVarianceRepository repository) : IQueryHandler<ListAssessmentVariancesByBoeQuery, Result<IReadOnlyList<AssessmentVarianceResponse>>>
{
    public async Task<Result<IReadOnlyList<AssessmentVarianceResponse>>> HandleAsync(ListAssessmentVariancesByBoeQuery query, CancellationToken ct)
    {
        IReadOnlyList<AssessmentVariance> variances = await repository.GetByBoeAsync(query.BoeId, ct);
        return Result.Success<IReadOnlyList<AssessmentVarianceResponse>>(variances.Select(ImportResponseFactory.ToVarianceResponse).ToArray());
    }
}

public sealed class ListPortChargesByFileHandler(IPortChargeRepository repository) : IQueryHandler<ListPortChargesByFileQuery, Result<IReadOnlyList<PortChargeResponse>>>
{
    public async Task<Result<IReadOnlyList<PortChargeResponse>>> HandleAsync(ListPortChargesByFileQuery query, CancellationToken ct)
    {
        IReadOnlyList<PortCharge> charges = await repository.GetByFileAsync(query.FileId, ct);
        return Result.Success<IReadOnlyList<PortChargeResponse>>(charges.Select(ImportResponseFactory.ToPortChargeResponse).ToArray());
    }
}

// ── Import Plan Query Handlers (BR-IP-01..06) ──────────────────────

public sealed class GetImportPlanHandler(IImportPlanRepository repository) : IQueryHandler<GetImportPlanQuery, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(GetImportPlanQuery query, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(query.PlanId, ct);
        return plan is null
            ? Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"))
            : Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class ListImportPlansHandler(
    IImportPlanRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListImportPlansQuery, Result<IReadOnlyList<ImportPlanResponse>>>
{
    public async Task<Result<IReadOnlyList<ImportPlanResponse>>> HandleAsync(ListImportPlansQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<ImportPlan> plans = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<ImportPlanResponse>>(plans.Select(ImportResponseFactory.ToPlanResponse).ToArray());
    }
}

public sealed class ListImportPlansByFiscalYearHandler(
    IImportPlanRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListImportPlansByFiscalYearQuery, Result<IReadOnlyList<ImportPlanResponse>>>
{
    public async Task<Result<IReadOnlyList<ImportPlanResponse>>> HandleAsync(ListImportPlansByFiscalYearQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<ImportPlan> plans = await repository.GetByFiscalYearAsync(tenantId, query.FiscalYear, ct);
        return Result.Success<IReadOnlyList<ImportPlanResponse>>(plans.Select(ImportResponseFactory.ToPlanResponse).ToArray());
    }
}

public sealed class GetPlanAdherenceReportHandler(IImportPlanRepository repository) : IQueryHandler<GetPlanAdherenceReportQuery, Result<PlanAdherenceReportResponse>>
{
    public async Task<Result<PlanAdherenceReportResponse>> HandleAsync(GetPlanAdherenceReportQuery query, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(query.PlanId, ct);
        if (plan is null)
            return Result.Failure<PlanAdherenceReportResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        decimal fobVariance = plan.TotalEstFob > 0
            ? (plan.Lines.Sum(l => l.ActualFob) - plan.TotalEstFob) / plan.TotalEstFob * 100
            : 0;
        decimal landedVariance = plan.TotalEstLanded > 0
            ? (plan.Lines.Sum(l => l.ActualLanded) - plan.TotalEstLanded) / plan.TotalEstLanded * 100
            : 0;

        var lineAdherence = plan.Lines.Select(l => new PlanLineAdherenceResponse(
            l.Id, l.Description, l.EstQty, l.ActualQty, l.EstFob, l.ActualFob,
            l.EstQty > 0 ? (l.ActualQty - l.EstQty) / l.EstQty * 100 : 0,
            l.EstFob > 0 ? (l.ActualFob - l.EstFob) / l.EstFob * 100 : 0
        )).ToArray();

        return Result.Success(new PlanAdherenceReportResponse(
            plan.Id, plan.PlanNumber, plan.FiscalYear,
            plan.TotalEstFob, plan.TotalEstLanded,
            plan.Lines.Sum(l => l.ActualFob), plan.Lines.Sum(l => l.ActualLanded),
            fobVariance, landedVariance, lineAdherence));
    }
}
public sealed class CreateCertificateOfOriginHandler(
    ICertificateOfOriginRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<CreateCertificateOfOriginQuery, Result<CertificateOfOriginResponse>>
{
    public async Task<Result<CertificateOfOriginResponse>> HandleAsync(CreateCertificateOfOriginQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        // Check if COO already exists for this file
        var existing = await repository.GetByFileAsync(request.FileId, ct);
        if (existing is not null)
            return Result.Failure<CertificateOfOriginResponse>(Error.NotFound("Coo.AlreadyExists", "Certificate of Origin already exists for this import file"));

        // Return success to indicate creation is needed - the command handler does the actual creation
        return Result.Success(new CertificateOfOriginResponse(
            Guid.NewGuid(), tenantId, request.FileId, request.CiId,
            request.Type, request.OriginCountry, request.DocumentNo, request.IssuerName,
            request.IssuedOn, request.ExpiryDate, false, false, null));
    }
}

public sealed class CheckCooOriginMismatchHandler(
    ICertificateOfOriginRepository repository) : IQueryHandler<CheckCooOriginMismatchQuery, Result<CertificateOfOriginResponse>>
{
    public async Task<Result<CertificateOfOriginResponse>> HandleAsync(CheckCooOriginMismatchQuery request, CancellationToken ct)
    {
        var coo = await repository.GetByFileAsync(request.FileId, ct);
        if (coo is null)
            return Result.Failure<CertificateOfOriginResponse>(Error.NotFound("Coo.NotFound", "Certificate of Origin not found for this file"));

        var result = coo.CheckOriginMismatch(request.CiOriginCountry);
        return result.IsFailure
            ? Result.Failure<CertificateOfOriginResponse>(result.Error)
            : Result.Success(ImportResponseFactory.ToCertificateOfOriginResponse(coo));
    }
}

public sealed class CreateCooIssuerRegistryHandler(
    ICooIssuerRegistryRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<CreateCooIssuerRegistryQuery, Result<CooIssuerRegistryResponse>>
{
    public async Task<Result<CooIssuerRegistryResponse>> HandleAsync(CreateCooIssuerRegistryQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var registry = await repository.GetByIdAsync(Guid.NewGuid(), ct);
        if (registry is not null)
            return Result.Success(ImportResponseFactory.ToCooIssuerRegistryResponse(registry));

        // Return success to indicate creation is needed - the command handler does the actual creation
        return Result.Success(new CooIssuerRegistryResponse(
            Guid.NewGuid(), tenantId, request.Country, request.IssuerName,
            request.LicenseNo, request.ValidFrom, request.ValidTo));
    }
}
