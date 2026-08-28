using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.OrgStructure.Domain.Events;

public sealed record OrgNodeCreatedDomainEvent(
    Guid EventId, Guid NodeId, Guid TenantId, string NodeType,
    string Code, string Name, DateTime OccurredAt) : IDomainEvent;

public sealed record OrgNodeUpdatedDomainEvent(
    Guid EventId, Guid NodeId, Guid TenantId, DateTime OccurredAt) : IDomainEvent;

public sealed record OrgNodeDeactivatedDomainEvent(
    Guid EventId, Guid NodeId, Guid TenantId, string NodeType,
    DateTime OccurredAt) : IDomainEvent;

public sealed record PositionCreatedDomainEvent(
    Guid EventId, Guid PositionId, Guid OrgNodeId, Guid TenantId,
    string Code, string Title, DateTime OccurredAt) : IDomainEvent;

public sealed record PositionAssignedDomainEvent(
    Guid EventId, Guid PositionId, Guid UserId, Guid TenantId,
    DateTime OccurredAt) : IDomainEvent;
