using Meetup.Modules.Payments.Domain.Entities;
using Meetup.Modules.Payments.Domain.Repositories;

namespace Meetup.Modules.Payments.Infrastructure.Repositories;

public sealed class MeetingFeePaymentRepository(PaymentsDbContext db) : IMeetingFeePaymentRepository
{
    public async Task AddAsync(MeetingFeePayment entity, CancellationToken ct)
        => await db.MeetingFeePayments.AddAsync(entity, ct);
}
