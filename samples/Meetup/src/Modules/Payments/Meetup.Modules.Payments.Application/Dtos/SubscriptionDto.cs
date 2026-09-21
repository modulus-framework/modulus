namespace Meetup.Modules.Payments.Application.Dtos;

public sealed record SubscriptionDto(
    Guid Id,
    string PayerLogin,
    DateTime ValidFrom,
    DateTime ValidTo,
    decimal Price,
    string Currency,
    bool IsActive);
