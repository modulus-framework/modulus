using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Queries;

public sealed record ListPaymentsQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<PaymentDto>>;