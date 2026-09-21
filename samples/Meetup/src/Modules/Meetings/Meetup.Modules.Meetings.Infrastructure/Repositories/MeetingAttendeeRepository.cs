using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Enums;
using Meetup.Modules.Meetings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meetup.Modules.Meetings.Infrastructure.Repositories;

public sealed class MeetingAttendeeRepository(MeetingsDbContext db) : IMeetingAttendeeRepository
{
    public async Task<bool> IsAttendingAsync(Guid meetingId, string login, CancellationToken ct)
        => await db.Attendees.AnyAsync(
            x => x.MeetingId == meetingId && x.Login == login && x.Status != AttendeeStatus.Declined, ct);

    public async Task<int> CountAttendeesAsync(Guid meetingId, CancellationToken ct)
        => await db.Attendees.CountAsync(
            x => x.MeetingId == meetingId && x.Status == AttendeeStatus.Attendee, ct);

    public async Task<IReadOnlyList<MeetingAttendee>> FindByMeetingAsync(Guid meetingId, CancellationToken ct)
        => await db.Attendees.Where(x => x.MeetingId == meetingId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(MeetingAttendee entity, CancellationToken ct)
        => await db.Attendees.AddAsync(entity, ct);
}
