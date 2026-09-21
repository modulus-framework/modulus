using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Meetings.Domain.Entities;

/// <summary>
/// Meeting group aggregate root (kgrzybek: MeetingGroup). Members are tracked
/// as a separate entity so EF Core needs no owned-type configuration.
/// </summary>
public sealed class MeetingGroup : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public string CreatorLogin { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Paid-through date of the organizer's subscription. A group without a
    /// covering subscription cannot organize meetings
    /// (kgrzybek: MeetingCanBeOrganizedOnlyByPayedGroupRule).
    /// </summary>
    public DateTime? PaymentValidUntil { get; private set; }

    private MeetingGroup()
    {
    }

    public static MeetingGroup CreateBasedOnProposal(
        string name,
        string description,
        string city,
        string countryCode,
        string creatorLogin)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return new MeetingGroup
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description,
            City = city,
            CountryCode = countryCode,
            CreatorLogin = creatorLogin,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsPaid(DateTime validUntil)
    {
        if (PaymentValidUntil is null || validUntil > PaymentValidUntil)
        {
            PaymentValidUntil = validUntil;
        }
    }

    public bool IsPaidFor(DateTime at) => PaymentValidUntil is not null && PaymentValidUntil > at;

    /// <summary>
    /// Attaches a domain event so <c>ModuleDbContext</c> enqueues it into the
    /// module outbox transactionally. Called by the Application handler with
    /// the module's integration event (which implements <c>IDomainEvent</c>).
    /// </summary>
    public void AddIntegrationEvent(IDomainEvent @event) => AddDomainEvent(@event);
}
