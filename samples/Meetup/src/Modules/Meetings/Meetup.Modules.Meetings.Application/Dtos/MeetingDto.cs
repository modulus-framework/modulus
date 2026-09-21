namespace Meetup.Modules.Meetings.Application.Dtos;

public sealed record MeetingDto(
    Guid Id,
    Guid GroupId,
    string Title,
    string Description,
    DateTime StartUtc,
    DateTime EndUtc,
    int? AttendeesLimit,
    int GuestsLimit,
    decimal EventFee,
    string EventFeeCurrency,
    string CreatorLogin);
