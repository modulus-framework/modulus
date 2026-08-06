using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

/// <summary>
/// Domain event raised when a permission is assigned to a role.
/// </summary>
public sealed record PermissionAssignedToRoleDomainEvent(
    RoleId RoleId,
    PermissionId PermissionId,
    UserId GrantedByUserId,
    DateTime AssignedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
