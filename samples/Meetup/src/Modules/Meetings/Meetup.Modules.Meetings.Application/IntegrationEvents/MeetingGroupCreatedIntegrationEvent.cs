using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Meetings.Application.IntegrationEvents;

/// <summary>Published when a meeting group is created (→ Payments/UI can react).</summary>
public sealed record MeetingGroupCreatedIntegrationEvent(
    Guid GroupId,
    string Name,
    string CreatorLogin)
    : IntegrationEventBase("meetings.meeting-group-created.v1"), IDomainEvent;
