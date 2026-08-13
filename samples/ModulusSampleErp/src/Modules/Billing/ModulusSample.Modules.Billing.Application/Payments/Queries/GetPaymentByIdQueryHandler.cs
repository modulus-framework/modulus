using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Queries;

public sealed class GetPaymentByIdQueryHandler(
    IPaymentRepository repository) : IQueryHandler<GetPaymentByIdQuery, PaymentDto?>
{
    public async Task<PaymentDto?> HandleAsync(
        GetPaymentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, cancellationToken);

        if (payment is null)
            return null;

        return new PaymentDto(
            payment.Id,
            payment.PaymentNumber,
            payment.InvoiceId,
            payment.PaymentDate,
            payment.Amount,
            payment.PaymentMethod,
            payment.Status,
            payment.ReferenceNumber);
    }
}