using Meetup.Modules.Payments.Domain.Entities;
using Meetup.Modules.Payments.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meetup.Modules.Payments.Infrastructure.Repositories;

public sealed class SubscriptionRepository(PaymentsDbContext db) : ISubscriptionRepository
{
    public async Task<Subscription?> FindLatestByPayerAsync(string payerLogin, CancellationToken ct)
        => await db.Subscriptions
            .Where(x => x.PayerLogin == payerLogin)
            .OrderByDescending(x => x.ValidTo)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Subscription>> FindByPayerAsync(string? payerLogin, CancellationToken ct)
    {
        var query = db.Subscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(payerLogin))
        {
            query = query.Where(x => x.PayerLogin == payerLogin);
        }

        return await query.OrderByDescending(x => x.ValidTo).ToListAsync(ct);
    }

    public async Task AddAsync(Subscription entity, CancellationToken ct)
        => await db.Subscriptions.AddAsync(entity, ct);
}
