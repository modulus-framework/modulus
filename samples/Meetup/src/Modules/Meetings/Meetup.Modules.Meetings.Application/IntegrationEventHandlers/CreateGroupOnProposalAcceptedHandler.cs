using Meetup.Modules.Administration.Application.IntegrationEvents;
using Meetup.Modules.Meetings.Application.IntegrationEvents;
using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Enums;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Meetings.Application.IntegrationEventHandlers;

/// <summary>
/// Consumes the accepted-proposal event and materializes the meeting group
/// (kgrzybek: ModulesIntegration — async only, no direct calls).
/// </summary>
public sealed class CreateGroupOnProposalAcceptedHandler(
    IMeetingGroupRepository groups,
    IMeetingGroupMemberRepository members,
    IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<MeetingGroupProposalAcceptedIntegrationEvent>
{
    public async Task HandleAsync(MeetingGroupProposalAcceptedIntegrationEvent @event, CancellationToken ct)
    {
        var exists = await groups.ExistsAsync(@event.Name, @event.ProposerLogin, ct);
        if (exists)
        {
            return;
        }

        var group = MeetingGroup.CreateBasedOnProposal(
            @event.Name, @event.Description, @event.City, @event.CountryCode, @event.ProposerLogin);

        // Transactional outbox: attach BEFORE CommitAsync so the group row and
        // the outbox row commit atomically; the OutboxProcessor relays it.
        group.AddIntegrationEvent(new MeetingGroupCreatedIntegrationEvent(
            group.Id, group.Name, group.CreatorLogin));

        await groups.AddAsync(group, ct);
        await members.AddAsync(MeetingGroupMember.Join(group.Id, @event.ProposerLogin, GroupMemberRole.Organizer), ct);
        await unitOfWork.CommitAsync(ct);
    }
}
