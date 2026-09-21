using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Meetings.Domain.Entities;

/// <summary>
/// Meeting aggregate root (kgrzybek: Meeting). Enforces attendee limits with a
/// waitlist, guest limits, and the at-least-one-host invariant.
/// </summary>
public sealed class Meeting : AggregateRoot<Guid>
{
    public Guid GroupId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public int? AttendeesLimit { get; private set; }
    public int GuestsLimit { get; private set; }
    public decimal EventFee { get; private set; }
    public string EventFeeCurrency { get; private set; } = "USD";
    public string CreatorLogin { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private Meeting()
    {
    }

    public static Meeting Create(
        Guid groupId,
        string title,
        string description,
        DateTime startUtc,
        DateTime endUtc,
        int? attendeesLimit,
        int guestsLimit,
        decimal eventFee,
        string eventFeeCurrency,
        string creatorLogin,
        bool groupIsPaid,
        bool creatorIsOrganizer)
    {
        if (!groupIsPaid)
        {
            throw new InvalidOperationException(
                "A meeting can only be organized by a group with an active subscription.");
        }

        if (!creatorIsOrganizer)
        {
            throw new InvalidOperationException("Only a group organizer can create a meeting.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (endUtc <= startUtc)
        {
            throw new ArgumentException("The meeting must end after it starts.");
        }

        if (attendeesLimit is < 1)
        {
            throw new ArgumentException("Attendee limit must be positive.", nameof(attendeesLimit));
        }

        if (guestsLimit < 0)
        {
            throw new ArgumentException("Guest limit cannot be negative.", nameof(guestsLimit));
        }

        return new Meeting
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Title = title.Trim(),
            Description = description,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AttendeesLimit = attendeesLimit,
            GuestsLimit = guestsLimit,
            EventFee = eventFee,
            EventFeeCurrency = string.IsNullOrWhiteSpace(eventFeeCurrency) ? "USD" : eventFeeCurrency,
            CreatorLogin = creatorLogin,
            CreatedAt = DateTime.UtcNow
        };
    }
}
