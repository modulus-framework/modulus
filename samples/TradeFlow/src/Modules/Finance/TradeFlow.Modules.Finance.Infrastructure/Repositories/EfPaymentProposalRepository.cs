using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

public sealed class EfPaymentProposalRepository : IPaymentProposalRepository
{
    private readonly FinanceDbContext _context;

    public EfPaymentProposalRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentProposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.PaymentProposals.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public void Add(PaymentProposal proposal) => _context.PaymentProposals.Add(proposal);

    public void Update(PaymentProposal proposal) => _context.PaymentProposals.Update(proposal);

    public void Delete(PaymentProposal proposal) => _context.PaymentProposals.Remove(proposal);
}