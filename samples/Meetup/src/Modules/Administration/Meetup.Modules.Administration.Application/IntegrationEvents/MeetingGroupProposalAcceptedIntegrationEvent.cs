using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Administration.Application.IntegrationEvents;

/// <summary>Published when an administrator accepts a proposal (→ Meetings creates the group).</summary>
public sealed record MeetingGroupProposalAcceptedIntegrationEvent(
    Guid ProposalId,
    string Name,
    string Description,
    string City,
    string CountryCode,
    string ProposerLogin)
    : IntegrationEventBase("administration.meeting-group-proposal-accepted.v1"), IDomainEvent;
