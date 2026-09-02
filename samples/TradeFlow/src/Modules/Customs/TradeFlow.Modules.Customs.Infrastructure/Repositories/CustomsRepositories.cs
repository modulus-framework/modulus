using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Modules.Customs.Infrastructure.Database;

namespace TradeFlow.Modules.Customs.Infrastructure.Repositories;

public sealed class EfHsCodeRepository(CustomsDbContext context) : IHsCodeRepository
{
    public Task<HsCode?> GetByCodeAsync(string code, CancellationToken ct = default)
        => context.HsCodes.FirstOrDefaultAsync(h => h.Code == code, ct);

    public Task<HsCode?> GetEffectiveAsync(string code, DateOnly date, CancellationToken ct = default)
        => context.HsCodes.FirstOrDefaultAsync(h =>
            h.Code == code &&
            h.EffectiveFrom <= date &&
            (h.EffectiveTo == null || h.EffectiveTo >= date), ct);

    public Task<IReadOnlyList<HsCode>> GetByChapterAsync(string chapterPrefix, CancellationToken ct = default)
        => context.HsCodes
            .Where(h => h.Code.StartsWith(chapterPrefix))
            .OrderBy(h => h.Code)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<HsCode>)t.Result, ct);

    public async Task AddAsync(HsCode hsCode, CancellationToken ct = default)
        => await context.HsCodes.AddAsync(hsCode, ct);
}

public sealed class EfDutyRateRepository(CustomsDbContext context) : IDutyRateRepository
{
    public async Task<IReadOnlyDictionary<DutyComponent, DutyRateRow>> GetEffectiveRatesAsync(
        string hsCode, DateOnly date, CancellationToken ct = default)
    {
        var rates = await context.DutyRates
            .Where(d => d.HsCode == hsCode && d.Status == DutyRateStatus.Approved && d.IsEffectiveOn(date))
            .AsNoTracking()
            .ToListAsync(ct);

        return rates.ToDictionary(
            d => d.Component,
            d => new DutyRateRow(d.Id, d.Component, d.Rate, d.SpecificRate, d.Uom, d.EffectiveFrom, d.EffectiveTo));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DutyComponent, DutyRateRow>>> GetEffectiveRatesForAsync(
        IReadOnlyList<string> hsCodes, DateOnly date, CancellationToken ct = default)
    {
        var rates = await context.DutyRates
            .Where(d => hsCodes.Contains(d.HsCode) && d.Status == DutyRateStatus.Approved && d.IsEffectiveOn(date))
            .AsNoTracking()
            .ToListAsync(ct);

        return rates
            .GroupBy(d => d.HsCode)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<DutyComponent, DutyRateRow>)g.ToDictionary(
                    d => d.Component,
                    d => new DutyRateRow(d.Id, d.Component, d.Rate, d.SpecificRate, d.Uom, d.EffectiveFrom, d.EffectiveTo)));
    }

    public Task<DutyRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.DutyRates.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<IReadOnlyList<DutyRate>> GetByHsCodeAsync(string hsCode, CancellationToken ct = default)
        => context.DutyRates
            .Where(d => d.HsCode == hsCode)
            .OrderBy(d => d.Component)
            .ThenByDescending(d => d.EffectiveFrom)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<DutyRate>)t.Result, ct);

    public async Task AddAsync(DutyRate rate, CancellationToken ct = default)
        => await context.DutyRates.AddAsync(rate, ct);

    public Task<bool> HasOverlappingAsync(DutyRate candidate, CancellationToken ct = default)
        => context.DutyRates.AnyAsync(d =>
            d.HsCode == candidate.HsCode &&
            d.Component == candidate.Component &&
            d.Status != DutyRateStatus.Rejected &&
            d.EffectiveFrom <= (candidate.EffectiveTo ?? DateOnly.MaxValue) &&
            candidate.EffectiveFrom <= (d.EffectiveTo ?? DateOnly.MaxValue), ct);
}

public sealed class EfSroBenefitRepository(CustomsDbContext context) : ISroBenefitRepository
{
    public async Task<IReadOnlyList<SroBenefitApplication>> GetActiveForAsync(
        string hsCode, Guid tenantId, DateOnly date, CancellationToken ct = default)
    {
        var benefits = await context.SroBenefits
            .Where(s => s.IsEffectiveOn(date))
            .AsNoTracking()
            .ToListAsync(ct);

        return benefits
            .Where(s => s.AppliesTo(hsCode, tenantId))
            .Select(s => new SroBenefitApplication(s.Id, s.Name, s.Type, s.OverrideRate, s.CapPercent))
            .ToList();
    }

    public Task<IReadOnlyList<SroBenefit>> GetActiveOnAsync(DateOnly date, CancellationToken ct = default)
        => context.SroBenefits
            .Where(s => s.IsEffectiveOn(date))
            .OrderBy(s => s.Name)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SroBenefit>)t.Result, ct);

    public Task<IReadOnlyList<SroBenefit>> GetAllAsync(CancellationToken ct = default)
        => context.SroBenefits
            .OrderBy(s => s.Name)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SroBenefit>)t.Result, ct);

    public async Task AddAsync(SroBenefit benefit, CancellationToken ct = default)
        => await context.SroBenefits.AddAsync(benefit, ct);
}

public sealed class EfBoeRepository(CustomsDbContext context) : IBoeRepository
{
    public async Task<BillOfEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.BillsOfEntry
            .AsSplitQuery()
            .Include(b => b.Lines)
                .ThenInclude(l => l.AssessedDutyLines)
            .Include(b => b.Lines)
                .ThenInclude(l => l.RateLineage)
            .Include(b => b.Challans)
            .Include(b => b.Milestones)
            .Include(b => b.Disputes)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public Task<IReadOnlyList<BillOfEntry>> GetByFileAsync(Guid fileId, CancellationToken ct = default)
        => context.BillsOfEntry
            .Where(b => b.FileId == fileId)
            .OrderByDescending(b => b.BoeDate)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<BillOfEntry>)t.Result, ct);

    public Task<IReadOnlyList<BillOfEntry>> GetAllAsync(CancellationToken ct = default)
        => context.BillsOfEntry
            .OrderByDescending(b => b.BoeDate)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<BillOfEntry>)t.Result, ct);

    public async Task<IReadOnlyList<BillOfEntry>> GetAssessedBetweenAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await context.BillsOfEntry
            .AsSplitQuery()
            .Include(b => b.Lines)
                .ThenInclude(l => l.AssessedDutyLines)
            .Where(b => b.BoeDate >= from && b.BoeDate <= to)
            .OrderBy(b => b.BoeDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BillOfEntry boe, CancellationToken ct = default)
        => await context.BillsOfEntry.AddAsync(boe, ct);

    public Task SaveAsync(BillOfEntry boe, CancellationToken ct = default)
    {
        context.BillsOfEntry.Update(boe);
        return Task.CompletedTask;
    }
}

public sealed class EfAitAtLedgerRepository(CustomsDbContext context) : IAitAtLedgerRepository
{
    public async Task AddAsync(AitAtLedgerEntry entry, CancellationToken ct = default)
        => await context.AitAtLedgerEntries.AddAsync(entry, ct);

    public async Task<IReadOnlyList<AitAtLedgerEntry>> GetForCompanyFyAsync(
        Guid companyId, int fiscalYear, CancellationToken ct = default)
    {
        return await context.AitAtLedgerEntries
            .Where(e => e.CompanyId == companyId && e.FiscalYear == fiscalYear)
            .OrderBy(e => e.BookedOn)
            .ToListAsync(ct);
    }
}

public sealed class EfDemurrageRepository(CustomsDbContext context) : IDemurrageRepository
{
    public async Task AddAsync(DemurrageAccrual accrual, CancellationToken ct = default)
        => await context.DemurrageAccruals.AddAsync(accrual, ct);

    public async Task<IReadOnlyList<DemurrageAccrual>> GetForFileAsync(Guid fileId, CancellationToken ct = default)
    {
        return await context.DemurrageAccruals
            .Where(d => d.FileId == fileId)
            .OrderBy(d => d.ContainerRef)
            .ToListAsync(ct);
    }
}

public sealed class EfItemHsMappingRepository(CustomsDbContext context) : IItemHsMappingRepository
{
    public Task<ItemHsMapping?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.ItemHsMappings.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<ItemHsMapping?> GetByItemAsync(Guid tenantId, Guid itemId, CancellationToken ct = default)
        => context.ItemHsMappings.FirstOrDefaultAsync(m =>
            m.TenantId == tenantId && m.ItemId == itemId && m.Status == HsMappingStatus.Approved, ct);

    public async Task<IReadOnlyList<ItemHsMapping>> GetByHsCodeAsync(Guid tenantId, string hsCode, CancellationToken ct = default)
        => await context.ItemHsMappings
            .Where(m => m.TenantId == tenantId && m.HsCode == hsCode)
            .OrderBy(m => m.ItemId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemHsMapping>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
        => await context.ItemHsMappings
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.HsCode)
            .ThenBy(m => m.ItemId)
            .ToListAsync(ct);

    public async Task AddAsync(ItemHsMapping mapping, CancellationToken ct = default)
        => await context.ItemHsMappings.AddAsync(mapping, ct);

    public async Task SaveAsync(ItemHsMapping mapping, CancellationToken ct = default)
    {
        context.ItemHsMappings.Update(mapping);
        await Task.CompletedTask;
    }
}