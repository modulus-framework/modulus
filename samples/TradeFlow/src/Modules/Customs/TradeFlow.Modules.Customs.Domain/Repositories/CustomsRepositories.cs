using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;

namespace TradeFlow.Modules.Customs.Domain.Repositories;

public interface IHsCodeRepository
{
    Task<HsCode?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<HsCode?> GetEffectiveAsync(string code, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<HsCode>> GetByChapterAsync(string chapterPrefix, CancellationToken ct = default);
    Task AddAsync(HsCode hsCode, CancellationToken ct = default);
}

public interface IDutyRateRepository
{
    /// <summary>Effective, approved rates for all components of a HS code on a date (BR-DS-01).</summary>
    Task<IReadOnlyDictionary<DutyComponent, DutyRateRow>> GetEffectiveRatesAsync(string hsCode, DateOnly date, CancellationToken ct = default);
    Task<DutyRate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DutyRate>> GetByHsCodeAsync(string hsCode, CancellationToken ct = default);
    Task AddAsync(DutyRate rate, CancellationToken ct = default);
    Task<bool> HasOverlappingAsync(DutyRate candidate, CancellationToken ct = default);
}

public interface ISroBenefitRepository
{
    /// <summary>Active SRO benefits matching the HS prefix for a tenant (BR-DS-05).</summary>
    Task<IReadOnlyList<SroBenefitApplication>> GetActiveForAsync(string hsCode, Guid tenantId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<SroBenefit>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(SroBenefit benefit, CancellationToken ct = default);
}

public interface IBoeRepository
{
    Task<BillOfEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BillOfEntry>> GetByFileAsync(Guid fileId, CancellationToken ct = default);
    Task<IReadOnlyList<BillOfEntry>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(BillOfEntry boe, CancellationToken ct = default);
    Task SaveAsync(BillOfEntry boe, CancellationToken ct = default);
}

public interface IAitAtLedgerRepository
{
    Task AddAsync(AitAtLedgerEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AitAtLedgerEntry>> GetForCompanyFyAsync(Guid companyId, int fiscalYear, CancellationToken ct = default);
}

public interface IDemurrageRepository
{
    Task AddAsync(DemurrageAccrual accrual, CancellationToken ct = default);
    Task<IReadOnlyList<DemurrageAccrual>> GetForFileAsync(Guid fileId, CancellationToken ct = default);
}

public interface IItemHsMappingRepository
{
    Task<ItemHsMapping?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ItemHsMapping?> GetByItemAsync(Guid tenantId, Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemHsMapping>> GetByHsCodeAsync(Guid tenantId, string hsCode, CancellationToken ct = default);
    Task<IReadOnlyList<ItemHsMapping>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(ItemHsMapping mapping, CancellationToken ct = default);
    Task SaveAsync(ItemHsMapping mapping, CancellationToken ct = default);
}