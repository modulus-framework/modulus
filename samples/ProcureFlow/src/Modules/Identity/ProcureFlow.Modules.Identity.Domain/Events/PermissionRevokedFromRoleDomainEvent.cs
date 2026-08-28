using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

/// <summary>
/// Domain event raised when a permission is revoked from a role.
/// </summary>
public sealed record PermissionRevokedFromRoleDomainEvent(
    RoleId RoleId,
    PermissionId PermissionId,
    UserId RevokedByUserId,
    DateTime RevokedAtUtc)
    : Modulus.Core.Abstractions.Domain.DomainEventBase;
