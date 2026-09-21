namespace Meetup.Modules.Payments.Application.Dtos;

public sealed record MeetingFeePaymentDto(
    Guid Id,
    string PayerLogin,
    Guid MeetingId,
    decimal Amount,
    string Currency,
    DateTime PaidAt);
