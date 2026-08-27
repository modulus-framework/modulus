using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Commands;

public sealed record ConfirmPaymentCommand(
    Guid PaymentId,
    string? ReferenceNumber = null) : ICommand<Result>;