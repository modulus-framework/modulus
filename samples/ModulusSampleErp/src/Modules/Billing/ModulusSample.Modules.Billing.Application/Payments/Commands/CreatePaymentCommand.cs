using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Commands;

public sealed record CreatePaymentCommand(
    string PaymentNumber,
    Guid InvoiceId,
    decimal Amount,
    string PaymentMethod) : ICommand<Result<Guid>>;