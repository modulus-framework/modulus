using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

public sealed class EfGrIrAccrualRepository : IGrIrAccrualRepository
{
    private readonly FinanceDbContext _context;

    public EfGrIrAccrualRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<GrIrAccrual?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals.FindAsync([id], ct);
    }

    public async Task<GrIrAccrual?> GetByGrnIdAsync(Guid grnId, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals.FirstOrDefaultAsync(x => x.GrnId == grnId, ct);
    }

    public async Task<IReadOnlyList<GrIrAccrual>> GetOpenAsync(CancellationToken ct = default)
    {
        return await _context.GrIrAccruals
            .Where(x => x.Status == GrIrAccrualStatus.Open)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GrIrAccrual>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals
            .Where(x => x.VendorId == vendorId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public void Add(GrIrAccrual accrual) => _context.GrIrAccruals.Add(accrual);

    public void Update(GrIrAccrual accrual) => _context.GrIrAccruals.Update(accrual);
}