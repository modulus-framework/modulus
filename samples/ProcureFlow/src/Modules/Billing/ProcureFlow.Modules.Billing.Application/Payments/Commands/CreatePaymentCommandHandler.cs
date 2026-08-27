using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Commands;

public sealed class CreatePaymentCommandHandler(
    IPaymentRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreatePaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreatePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var paymentId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = Payment.Create(
            paymentId,
            request.PaymentNumber,
            request.InvoiceId,
            request.Amount,
            request.PaymentMethod,
            orgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(paymentId);
    }
}