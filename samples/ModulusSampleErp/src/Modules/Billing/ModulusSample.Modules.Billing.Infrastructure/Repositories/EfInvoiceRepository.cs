using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Modules.Billing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Infrastructure.Repositories;

public sealed class EfInvoiceRepository(BillingDbContext context) : IInvoiceRepository
{
    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Invoices.FindAsync([id], ct);
    }

    public async Task<PagedResult<Invoice>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.Invoices.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var invoices = await query
            .OrderByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Invoice>(invoices, totalCount, page, pageSize);
    }

    public async Task AddAsync(Invoice invoice, CancellationToken ct = default)
    {
        await context.Invoices.AddAsync(invoice, ct);
    }
}