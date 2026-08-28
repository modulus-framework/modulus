using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

/// <summary>
/// Domain event raised when a permission is assigned to a role.
/// </summary>
public sealed record PermissionAssignedToRoleDomainEvent(
    RoleId RoleId,
    PermissionId PermissionId,
    UserId GrantedByUserId,
    DateTime AssignedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
