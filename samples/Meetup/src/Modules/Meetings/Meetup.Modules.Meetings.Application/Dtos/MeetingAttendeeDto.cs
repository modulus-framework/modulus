namespace Meetup.Modules.Meetings.Application.Dtos;

public sealed record MeetingAttendeeDto(
    Guid Id,
    Guid MeetingId,
    string Login,
    int GuestsCount,
    bool IsHost,
    string Status);
