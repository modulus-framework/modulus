using ProcureFlow.Modules.Import.Domain.Entities;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.Import.Domain.Repositories;

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