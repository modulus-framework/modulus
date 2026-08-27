using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;

namespace ModulusSample.Modules.Billing.Application.Payments.Queries;

public sealed record GetPaymentByIdQuery(Guid PaymentId) : IQuery<PaymentDto?>;