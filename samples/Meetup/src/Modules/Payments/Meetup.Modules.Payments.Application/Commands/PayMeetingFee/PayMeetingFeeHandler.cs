using Meetup.Modules.Payments.Domain.Entities;
using Meetup.Modules.Payments.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Application.Commands.PayMeetingFee;

public sealed class PayMeetingFeeHandler(
    IMeetingFeePaymentRepository payments, IUnitOfWork unitOfWork)
    : ICommandHandler<PayMeetingFeeCommand, Guid>
{
    public async Task<Guid> HandleAsync(PayMeetingFeeCommand command, CancellationToken ct)
    {
        var payment = MeetingFeePayment.Pay(
            command.PayerLogin, command.MeetingId, command.Amount, command.Currency);
        await payments.AddAsync(payment, ct);
        await unitOfWork.CommitAsync(ct);
        return payment.Id;
    }
}
