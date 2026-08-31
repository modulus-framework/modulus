using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Import.Domain.Entities;
using TradeFlow.Modules.Import.Domain.Repositories;
using TradeFlow.Modules.Import.Infrastructure.Database;

namespace TradeFlow.Modules.Import.Infrastructure.Repositories;

public sealed class EfImportFileRepository(ImportDbContext db) : IImportFileRepository
{
    public Task<ImportFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ImportFiles
            .AsSplitQuery()
            .Include(f => f.Milestones)
            .Include(f => f.Containers)
            .Include(f => f.CostEntries)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<ImportFile?> GetByNumberAsync(Guid tenantId, string fileNumber, CancellationToken ct = default) =>
        db.ImportFiles
            .AsSplitQuery()
            .Include(f => f.Milestones)
            .Include(f => f.Containers)
            .Include(f => f.CostEntries)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FileNumber == fileNumber, ct);

    public Task<IReadOnlyList<ImportFile>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        db.ImportFiles
            .AsSplitQuery()
            .Include(f => f.Milestones)
            .Include(f => f.Containers)
            .Include(f => f.CostEntries)
            .Include(f => f.Documents)
            .Where(f => f.TenantId == tenantId)
            .OrderByDescending(f => f.FiscalYear)
            .ThenByDescending(f => f.Sequence)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ImportFile>)t.Result, ct);

    public async Task AddAsync(ImportFile file, CancellationToken ct = default) =>
        await db.ImportFiles.AddAsync(file, ct);

    public async Task SaveAsync(ImportFile file, CancellationToken ct = default) =>
        await Task.FromResult(db.ImportFiles.Update(file));

    public async Task<int> NextSequenceAsync(Guid tenantId, Guid companyId, int fiscalYear, CancellationToken ct = default)
    {
        int max = await db.ImportFiles
            .Where(f => f.TenantId == tenantId && f.CompanyId == companyId && f.FiscalYear == fiscalYear)
            .MaxAsync(f => (int?)f.Sequence, ct) ?? 0;
        return max + 1;
    }
}

public sealed class EfProformaInvoiceRepository(ImportDbContext db) : IProformaInvoiceRepository
{
    public Task<ProformaInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ProformaInvoices.AsSplitQuery().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<IReadOnlyList<ProformaInvoice>> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.ProformaInvoices.AsSplitQuery().Include(p => p.Lines)
            .Where(p => p.FileId == fileId)
            .OrderByDescending(p => p.IssuedOn)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ProformaInvoice>)t.Result, ct);

    public async Task AddAsync(ProformaInvoice pi, CancellationToken ct = default) =>
        await db.ProformaInvoices.AddAsync(pi, ct);

    public async Task SaveAsync(ProformaInvoice pi, CancellationToken ct = default) =>
        await Task.FromResult(db.ProformaInvoices.Update(pi));
}

public sealed class EfCommercialInvoiceRepository(ImportDbContext db) : ICommercialInvoiceRepository
{
    public Task<CommercialInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CommercialInvoices.AsSplitQuery().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<IReadOnlyList<CommercialInvoice>> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.CommercialInvoices.AsSplitQuery().Include(c => c.Lines)
            .Where(c => c.FileId == fileId)
            .OrderByDescending(c => c.IssuedOn)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<CommercialInvoice>)t.Result, ct);

    public async Task AddAsync(CommercialInvoice ci, CancellationToken ct = default) =>
        await db.CommercialInvoices.AddAsync(ci, ct);

    public async Task SaveAsync(CommercialInvoice ci, CancellationToken ct = default) =>
        await Task.FromResult(db.CommercialInvoices.Update(ci));
}

public sealed class EfPackingListRepository(ImportDbContext db) : IPackingListRepository
{
    public Task<PackingList?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.PackingLists.AsSplitQuery().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddAsync(PackingList pl, CancellationToken ct = default) =>
        await db.PackingLists.AddAsync(pl, ct);

    public async Task SaveAsync(PackingList pl, CancellationToken ct = default) =>
        await Task.FromResult(db.PackingLists.Update(pl));
}

public sealed class EfShipmentRepository(ImportDbContext db) : IShipmentRepository
{
    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Shipments.AsSplitQuery().Include(s => s.Milestones).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(Shipment shipment, CancellationToken ct = default) =>
        await db.Shipments.AddAsync(shipment, ct);

    public async Task SaveAsync(Shipment shipment, CancellationToken ct = default) =>
        await Task.FromResult(db.Shipments.Update(shipment));
}

public sealed class EfTransportDocumentRepository(ImportDbContext db) : ITransportDocumentRepository
{
    public Task<TransportDocument?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.TransportDocuments.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<IReadOnlyList<TransportDocument>> GetByShipmentAsync(Guid shipmentId, CancellationToken ct = default) =>
        db.TransportDocuments
            .Where(t => t.ShipmentId == shipmentId)
            .OrderByDescending(t => t.IssueDate)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<TransportDocument>)t.Result, ct);

    public async Task AddAsync(TransportDocument document, CancellationToken ct = default) =>
        await db.TransportDocuments.AddAsync(document, ct);

    public async Task SaveAsync(TransportDocument document, CancellationToken ct = default) =>
        await Task.FromResult(db.TransportDocuments.Update(document));
}

public sealed class EfFreightCostRepository(ImportDbContext db) : IFreightCostRepository
{
    public Task<FreightCost?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.FreightCosts.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<IReadOnlyList<FreightCost>> GetByShipmentAsync(Guid shipmentId, CancellationToken ct = default) =>
        db.FreightCosts
            .Where(f => f.ShipmentId == shipmentId)
            .OrderBy(f => f.CostType)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<FreightCost>)t.Result, ct);

    public Task<IReadOnlyList<FreightCost>> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.FreightCosts
            .Where(f => f.FileId == fileId)
            .OrderBy(f => f.Stage)
            .ThenBy(f => f.CostType)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<FreightCost>)t.Result, ct);

    public async Task AddAsync(FreightCost cost, CancellationToken ct = default) =>
        await db.FreightCosts.AddAsync(cost, ct);
}

public sealed class EfInsurancePolicyRepository(ImportDbContext db) : IInsurancePolicyRepository
{
    public Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.InsurancePolicies.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<InsurancePolicy?> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.InsurancePolicies.FirstOrDefaultAsync(i => i.FileId == fileId, ct);

    public async Task AddAsync(InsurancePolicy policy, CancellationToken ct = default) =>
        await db.InsurancePolicies.AddAsync(policy, ct);
}

public sealed class EfImportPermitRepository(ImportDbContext db) : IImportPermitRepository
{
    public Task<ImportPermit?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Permits.AsSplitQuery().Include(p => p.Utilizations).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<ImportPermit?> GetByCategoryAsync(Guid tenantId, Guid companyId, string category, CancellationToken ct = default) =>
        db.Permits
            .AsSplitQuery()
            .Include(p => p.Utilizations)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.CompanyId == companyId && p.Category == category, ct);

    public async Task AddAsync(ImportPermit permit, CancellationToken ct = default) =>
        await db.Permits.AddAsync(permit, ct);

    public async Task SaveAsync(ImportPermit permit, CancellationToken ct = default) =>
        await Task.FromResult(db.Permits.Update(permit));
}

public sealed class EfBillOfEntryRepository(ImportDbContext db) : IBillOfEntryRepository
{
    public Task<BillOfEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.BillsOfEntry
            .AsSplitQuery()
            .Include(b => b.Lines)
            .Include(b => b.DutyLines)
            .Include(b => b.Milestones)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<BillOfEntry?> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.BillsOfEntry
            .AsSplitQuery()
            .Include(b => b.Lines)
            .Include(b => b.DutyLines)
            .Include(b => b.Milestones)
            .FirstOrDefaultAsync(b => b.FileId == fileId, ct);

    public async Task AddAsync(BillOfEntry boe, CancellationToken ct = default) =>
        await db.BillsOfEntry.AddAsync(boe, ct);

    public async Task SaveAsync(BillOfEntry boe, CancellationToken ct = default) =>
        await Task.FromResult(db.BillsOfEntry.Update(boe));
}

public sealed class EfAssessmentVarianceRepository(ImportDbContext db) : IAssessmentVarianceRepository
{
    public Task<AssessmentVariance?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.AssessmentVariances.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<IReadOnlyList<AssessmentVariance>> GetByBoeAsync(Guid boeId, CancellationToken ct = default) =>
        db.AssessmentVariances
            .Where(v => v.BoeId == boeId)
            .OrderBy(v => v.Component)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<AssessmentVariance>)t.Result, ct);

    public async Task AddAsync(AssessmentVariance variance, CancellationToken ct = default) =>
        await db.AssessmentVariances.AddAsync(variance, ct);
}

public sealed class EfPortChargeRepository(ImportDbContext db) : IPortChargeRepository
{
    public Task<PortCharge?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.PortCharges.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<IReadOnlyList<PortCharge>> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.PortCharges
            .Where(c => c.FileId == fileId)
            .OrderBy(c => c.ChargedOn)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<PortCharge>)t.Result, ct);

    public async Task AddAsync(PortCharge charge, CancellationToken ct = default) =>
        await db.PortCharges.AddAsync(charge, ct);
}

public sealed class EfCnfAgentRepository(ImportDbContext db) : ICnfAgentRepository
{
    public Task<CnfAgent?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CnfAgents.AsSplitQuery().Include(a => a.ChargeBills).FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(CnfAgent agent, CancellationToken ct = default) =>
        await db.CnfAgents.AddAsync(agent, ct);

    public async Task SaveAsync(CnfAgent agent, CancellationToken ct = default) =>
        await Task.FromResult(db.CnfAgents.Update(agent));
}

public sealed class EfImportPlanRepository(ImportDbContext db) : IImportPlanRepository
{
    public Task<ImportPlan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ImportPlans
            .AsSplitQuery()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<IReadOnlyList<ImportPlan>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        db.ImportPlans
            .AsSplitQuery()
            .Include(p => p.Lines)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.FiscalYear)
            .ThenBy(p => p.PlanNumber)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ImportPlan>)t.Result, ct);

    public Task<IReadOnlyList<ImportPlan>> GetByFiscalYearAsync(Guid tenantId, int fiscalYear, CancellationToken ct = default) =>
        db.ImportPlans
            .AsSplitQuery()
            .Include(p => p.Lines)
            .Where(p => p.TenantId == tenantId && p.FiscalYear == fiscalYear)
            .OrderBy(p => p.PlanNumber)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ImportPlan>)t.Result, ct);

    public async Task AddAsync(ImportPlan plan, CancellationToken ct = default) =>
        await db.ImportPlans.AddAsync(plan, ct);

    public async Task SaveAsync(ImportPlan plan, CancellationToken ct = default) =>
        await Task.FromResult(db.ImportPlans.Update(plan));
}

public sealed class EfCertificateOfOriginRepository(ImportDbContext db) : ICertificateOfOriginRepository
{
    public Task<CertificateOfOrigin?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CertificatesOfOrigin.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CertificateOfOrigin?> GetByFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.CertificatesOfOrigin.FirstOrDefaultAsync(c => c.FileId == fileId, ct);

    public async Task<IReadOnlyList<CertificateOfOrigin>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.CertificatesOfOrigin
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.IssuedOn)
            .ToListAsync(ct);

    public async Task AddAsync(CertificateOfOrigin coo, CancellationToken ct = default) =>
        await db.CertificatesOfOrigin.AddAsync(coo, ct);

    public async Task SaveAsync(CertificateOfOrigin coo, CancellationToken ct = default) =>
        await Task.FromResult(db.CertificatesOfOrigin.Update(coo));
}

public sealed class EfCooIssuerRegistryRepository(ImportDbContext db) : ICooIssuerRegistryRepository
{
    public Task<CooIssuerRegistry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CooIssuerRegistries.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<CooIssuerRegistry>> GetByCountryAsync(Guid tenantId, string country, CancellationToken ct = default) =>
        await db.CooIssuerRegistries
            .Where(r => r.TenantId == tenantId && r.Country == country)
            .OrderBy(r => r.IssuerName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CooIssuerRegistry>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.CooIssuerRegistries
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Country)
            .ThenBy(r => r.IssuerName)
            .ToListAsync(ct);

    public async Task AddAsync(CooIssuerRegistry registry, CancellationToken ct = default) =>
        await db.CooIssuerRegistries.AddAsync(registry, ct);

    public async Task SaveAsync(CooIssuerRegistry registry, CancellationToken ct = default) =>
        await Task.FromResult(db.CooIssuerRegistries.Update(registry));
}