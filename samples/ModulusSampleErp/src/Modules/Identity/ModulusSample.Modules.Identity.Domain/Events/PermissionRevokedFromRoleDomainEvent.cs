using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

/// <summary>
/// Domain event raised when a permission is revoked from a role.
/// </summary>
public sealed record PermissionRevokedFromRoleDomainEvent(
    RoleId RoleId,
    PermissionId PermissionId,
    UserId RevokedByUserId,
    DateTime RevokedAtUtc)
    : Modulus.Core.Abstractions.Domain.DomainEventBase;
