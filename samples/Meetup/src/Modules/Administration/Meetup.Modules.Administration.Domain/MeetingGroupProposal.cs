using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Administration.Domain;

/// <summary>
/// A member's proposal to create a meeting group. Administrators accept or
/// reject it; acceptance raises an integration event that the Meetings module
/// consumes to create the group (kgrzybek: Administration bounded context).
/// </summary>
public sealed class MeetingGroupProposal : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public string ProposerLogin { get; private set; } = string.Empty;
    public ProposalStatus Status { get; private set; }
    public DateTime ProposedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }

    private MeetingGroupProposal()
    {
    }

    public static MeetingGroupProposal ProposeNew(
        string name,
        string description,
        string city,
        string countryCode,
        string proposerLogin)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return new MeetingGroupProposal
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description,
            City = city,
            CountryCode = countryCode,
            ProposerLogin = proposerLogin,
            Status = ProposalStatus.Proposed,
            ProposedAt = DateTime.UtcNow
        };
    }

    public void Accept()
    {
        EnsurePending();
        Status = ProposalStatus.Accepted;
        DecidedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        EnsurePending();
        Status = ProposalStatus.Rejected;
        DecidedAt = DateTime.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status is not ProposalStatus.Proposed)
        {
            throw new InvalidOperationException("Only a pending proposal can be decided.");
        }
    }

    /// <summary>
    /// Attaches a domain event so <c>ModuleDbContext</c> enqueues it into the
    /// module outbox transactionally. Called by the Application handler with
    /// the module's integration event (which implements <c>IDomainEvent</c>).
    /// </summary>
    public void AddIntegrationEvent(IDomainEvent @event) => AddDomainEvent(@event);
}
