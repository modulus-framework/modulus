using Meetup.Modules.Payments.Domain.Entities;

namespace Meetup.Modules.Payments.Domain.Repositories;

public interface IMeetingFeePaymentRepository
{
    Task AddAsync(MeetingFeePayment entity, CancellationToken ct);
}
