using TradeFlow.Modules.Import.Domain.Entities;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.Import.Domain.Repositories;

public interface IImportFileRepository
{
    Task<ImportFile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ImportFile?> GetByNumberAsync(Guid tenantId, string fileNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ImportFile>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(ImportFile file, CancellationToken ct = default);
    Task SaveAsync(ImportFile file, CancellationToken ct = default);
    Task<int> NextSequenceAsync(Guid tenantId, Guid companyId, int fiscalYear, CancellationToken ct = default);
}

public interface IProformaInvoiceRepository
{
    Task<ProformaInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ProformaInvoice>> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(ProformaInvoice pi, CancellationToken ct = default);
    Task SaveAsync(ProformaInvoice pi, CancellationToken ct = default);
}

public interface ICommercialInvoiceRepository
{
    Task<CommercialInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CommercialInvoice>> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(CommercialInvoice ci, CancellationToken ct = default);
    Task SaveAsync(CommercialInvoice ci, CancellationToken ct = default);
}

public interface IPackingListRepository
{
    Task<PackingList?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PackingList pl, CancellationToken ct = default);
    Task SaveAsync(PackingList pl, CancellationToken ct = default);
}

public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Shipment shipment, CancellationToken ct = default);
    Task SaveAsync(Shipment shipment, CancellationToken ct = default);
}

public interface ITransportDocumentRepository
{
    Task<TransportDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TransportDocument>> GetByShipmentAsync(Guid shipmentId, CancellationToken ct = default);
    Task AddAsync(TransportDocument document, CancellationToken ct = default);
    Task SaveAsync(TransportDocument document, CancellationToken ct = default);
}

public interface IFreightCostRepository
{
    Task<FreightCost?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FreightCost>> GetByShipmentAsync(Guid shipmentId, CancellationToken ct = default);
    Task<IReadOnlyList<FreightCost>> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(FreightCost cost, CancellationToken ct = default);
}

public interface IInsurancePolicyRepository
{
    Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InsurancePolicy?> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(InsurancePolicy policy, CancellationToken ct = default);
}

public interface IImportPermitRepository
{
    Task<ImportPermit?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ImportPermit?> GetByCategoryAsync(Guid tenantId, Guid companyId, string category, CancellationToken ct = default);
    Task AddAsync(ImportPermit permit, CancellationToken ct = default);
    Task SaveAsync(ImportPermit permit, CancellationToken ct = default);
}

public interface IBillOfEntryRepository
{
    Task<BillOfEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BillOfEntry?> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(BillOfEntry boe, CancellationToken ct = default);
    Task SaveAsync(BillOfEntry boe, CancellationToken ct = default);
}

public interface IAssessmentVarianceRepository
{
    Task<AssessmentVariance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AssessmentVariance>> GetByBoeAsync(Guid boeId, CancellationToken ct = default);
    Task AddAsync(AssessmentVariance variance, CancellationToken ct = default);
}

public interface IPortChargeRepository
{
    Task<PortCharge?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PortCharge>> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task AddAsync(PortCharge charge, CancellationToken ct = default);
}

public interface ICnfAgentRepository
{
    Task<CnfAgent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CnfAgent agent, CancellationToken ct = default);
    Task SaveAsync(CnfAgent agent, CancellationToken ct = default);
}

public interface IImportPlanRepository
{
    Task<ImportPlan?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ImportPlan>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ImportPlan>> GetByFiscalYearAsync(Guid tenantId, int fiscalYear, CancellationToken ct = default);
    Task AddAsync(ImportPlan plan, CancellationToken ct = default);
    Task SaveAsync(ImportPlan plan, CancellationToken ct = default);
}

public interface ICertificateOfOriginRepository
{
    Task<CertificateOfOrigin?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CertificateOfOrigin?> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateOfOrigin>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(CertificateOfOrigin coo, CancellationToken ct = default);
    Task SaveAsync(CertificateOfOrigin coo, CancellationToken ct = default);
}

public interface ICooIssuerRegistryRepository
{
    Task<CooIssuerRegistry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CooIssuerRegistry>> GetByCountryAsync(Guid tenantId, string country, CancellationToken ct = default);
    Task<IReadOnlyList<CooIssuerRegistry>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(CooIssuerRegistry registry, CancellationToken ct = default);
    Task SaveAsync(CooIssuerRegistry registry, CancellationToken ct = default);
}
