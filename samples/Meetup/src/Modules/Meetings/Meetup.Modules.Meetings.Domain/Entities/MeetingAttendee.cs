using Meetup.Modules.Meetings.Domain.Enums;
using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Meetings.Domain.Entities;

/// <summary>One member's participation in a meeting (attendee, waitlist, or declined).</summary>
public sealed class MeetingAttendee : AggregateRoot<Guid>
{
    public Guid MeetingId { get; private set; }
    public string Login { get; private set; } = string.Empty;
    public int GuestsCount { get; private set; }
    public bool IsHost { get; private set; }
    public AttendeeStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private MeetingAttendee()
    {
    }

    public static MeetingAttendee Join(
        Guid meetingId,
        string login,
        int guestsCount,
        int guestsLimit,
        int currentAttendeeCount,
        int? attendeesLimit,
        bool alreadyAttending,
        bool isHost = false)
    {
        if (alreadyAttending)
        {
            throw new InvalidOperationException("A member cannot attend the same meeting twice.");
        }

        if (guestsCount < 0 || guestsCount > guestsLimit)
        {
            throw new InvalidOperationException($"Guests must be between 0 and {guestsLimit}.");
        }

        var status = attendeesLimit is not null && currentAttendeeCount >= attendeesLimit
            ? AttendeeStatus.Waitlist
            : AttendeeStatus.Attendee;

        return new MeetingAttendee
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            Login = login,
            GuestsCount = guestsCount,
            IsHost = isHost,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Decline() => Status = AttendeeStatus.Declined;
}
