using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Modules.Billing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Infrastructure.Repositories;

public sealed class EfCreditNoteRepository(BillingDbContext context) : ICreditNoteRepository
{
    public async Task<CreditNote?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.CreditNotes.FindAsync([id], ct);
    }

    public async Task<PagedResult<CreditNote>> ListAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = context.CreditNotes.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var creditNotes = await query
            .OrderByDescending(cn => cn.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CreditNote>(creditNotes, totalCount, page, pageSize);
    }

    public async Task AddAsync(CreditNote creditNote, CancellationToken ct = default)
    {
        await context.CreditNotes.AddAsync(creditNote, ct);
    }
}