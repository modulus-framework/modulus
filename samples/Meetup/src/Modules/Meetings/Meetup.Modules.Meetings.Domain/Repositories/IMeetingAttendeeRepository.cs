using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Domain.Repositories;

public interface IMeetingAttendeeRepository
{
    Task<bool> IsAttendingAsync(Guid meetingId, string login, CancellationToken ct);
    Task<int> CountAttendeesAsync(Guid meetingId, CancellationToken ct);
    Task<IReadOnlyList<MeetingAttendee>> FindByMeetingAsync(Guid meetingId, CancellationToken ct);
    Task AddAsync(MeetingAttendee entity, CancellationToken ct);
}
