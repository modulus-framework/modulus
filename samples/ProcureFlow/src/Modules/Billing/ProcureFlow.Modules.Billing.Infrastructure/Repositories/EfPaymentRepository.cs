using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Modules.Billing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Infrastructure.Repositories;

public sealed class EfPaymentRepository(BillingDbContext context) : IPaymentRepository
{
    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Payments.FindAsync([id], ct);
    }

    public async Task<PagedResult<Payment>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.Payments.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var payments = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Payment>(payments, totalCount, page, pageSize);
    }

    public async Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        await context.Payments.AddAsync(payment, ct);
    }
}