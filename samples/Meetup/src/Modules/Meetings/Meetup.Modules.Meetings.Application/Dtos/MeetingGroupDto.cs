namespace Meetup.Modules.Meetings.Application.Dtos;

public sealed record MeetingGroupDto(
    Guid Id,
    string Name,
    string Description,
    string City,
    string CountryCode,
    string CreatorLogin,
    DateTime? PaymentValidUntil,
    DateTime CreatedAt);
