using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Commands;

public sealed class ConfirmPaymentCommandHandler(
    IPaymentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ConfirmPaymentCommand, Result>
{
    public async Task<Result> HandleAsync(
        ConfirmPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, cancellationToken);

        if (payment is null)
            return Result.Failure(Error.NotFound("Payment.NotFound", "Payment not found"));

        var result = payment.Confirm(request.ReferenceNumber);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}